using System;
using System.Threading.Tasks;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Serilog;

namespace Nexum.Tests.E2E.Orchestration
{
    public class IamProvisioner : IDisposable
    {
        private readonly AmazonIdentityManagementServiceClient _iamClient;
        private readonly string _instanceProfileName;
        private readonly ILogger _logger;
        private readonly string _roleName;

        public IamProvisioner(string roleName = null, string instanceProfileName = null)
        {
            _logger = Log.ForContext<IamProvisioner>();
            _iamClient = new AmazonIdentityManagementServiceClient();
            _roleName = roleName ?? AwsConfig.IamRoleName;
            _instanceProfileName = instanceProfileName ?? AwsConfig.InstanceProfileName;
        }

        public string InstanceProfileArn { get; private set; }

        public void Dispose()
        {
            _iamClient?.Dispose();
        }

        public async Task<string> ProvisionAsync()
        {
            await CreateRoleAsync();
            await AttachPolicyAsync();
            await CreateInstanceProfileAsync();
            await AddRoleToInstanceProfileAsync();

            _logger.Information("Waiting for IAM propagation (15 seconds)...");
            await Task.Delay(TimeSpan.FromSeconds(15));

            return InstanceProfileArn;
        }

        private async Task CreateRoleAsync()
        {
            const string assumeRolePolicyDocument = @"{
                ""Version"": ""2012-10-17"",
                ""Statement"": [
                    {
                        ""Effect"": ""Allow"",
                        ""Principal"": {
                            ""Service"": ""ec2.amazonaws.com""
                        },
                        ""Action"": ""sts:AssumeRole""
                    }
                ]
            }";

            try
            {
                await _iamClient.GetRoleAsync(new GetRoleRequest { RoleName = _roleName });
                _logger.Information("IAM role {RoleName} already exists", _roleName);
                return;
            }
            catch (NoSuchEntityException)
            {
            }

            _logger.Information("Creating IAM role {RoleName}", _roleName);

            var request = new CreateRoleRequest
            {
                RoleName = _roleName,
                AssumeRolePolicyDocument = assumeRolePolicyDocument,
                Description = "Role for Nexum E2E test EC2 instances",
                Tags =
                [
                    new Tag { Key = AwsConfig.ResourceTagKey, Value = AwsConfig.ResourceTagValue }
                ]
            };

            await _iamClient.CreateRoleAsync(request);
            _logger.Information("IAM role {RoleName} created", _roleName);
        }

        private async Task AttachPolicyAsync()
        {
            _logger.Information("Attaching policy {PolicyArn} to role {RoleName}",
                AwsConfig.IamPolicyArn, _roleName);

            try
            {
                await _iamClient.AttachRolePolicyAsync(new AttachRolePolicyRequest
                {
                    RoleName = _roleName,
                    PolicyArn = AwsConfig.IamPolicyArn
                });
            }
            catch (Exception ex) when (ex.Message.Contains("already attached"))
            {
                _logger.Debug("Policy already attached to role");
            }
        }

        private async Task CreateInstanceProfileAsync()
        {
            try
            {
                var existing = await _iamClient.GetInstanceProfileAsync(
                    new GetInstanceProfileRequest { InstanceProfileName = _instanceProfileName });
                InstanceProfileArn = existing.InstanceProfile.Arn;
                _logger.Information("Instance profile {ProfileName} already exists", _instanceProfileName);
                return;
            }
            catch (NoSuchEntityException)
            {
            }

            _logger.Information("Creating instance profile {ProfileName}", _instanceProfileName);

            var response = await _iamClient.CreateInstanceProfileAsync(new CreateInstanceProfileRequest
            {
                InstanceProfileName = _instanceProfileName,
                Tags =
                [
                    new Tag { Key = AwsConfig.ResourceTagKey, Value = AwsConfig.ResourceTagValue }
                ]
            });

            InstanceProfileArn = response.InstanceProfile.Arn;
            _logger.Information("Instance profile created: {Arn}", InstanceProfileArn);
        }

        private async Task AddRoleToInstanceProfileAsync()
        {
            try
            {
                await _iamClient.AddRoleToInstanceProfileAsync(new AddRoleToInstanceProfileRequest
                {
                    InstanceProfileName = _instanceProfileName,
                    RoleName = _roleName
                });
                _logger.Information("Added role {RoleName} to instance profile {ProfileName}",
                    _roleName, _instanceProfileName);
            }
            catch (LimitExceededException)
            {
                _logger.Debug("Role already added to instance profile");
            }
            catch (EntityAlreadyExistsException)
            {
                _logger.Debug("Role already added to instance profile");
            }
        }

        public async Task CleanupAsync()
        {
            _logger.Information("Cleaning up IAM resources...");

            try
            {
                await _iamClient.RemoveRoleFromInstanceProfileAsync(new RemoveRoleFromInstanceProfileRequest
                {
                    InstanceProfileName = _instanceProfileName,
                    RoleName = _roleName
                });
                _logger.Debug("Removed role from instance profile");
            }
            catch (NoSuchEntityException)
            {
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to remove role from instance profile");
            }

            try
            {
                await _iamClient.DeleteInstanceProfileAsync(new DeleteInstanceProfileRequest
                {
                    InstanceProfileName = _instanceProfileName
                });
                _logger.Debug("Deleted instance profile {ProfileName}", _instanceProfileName);
            }
            catch (NoSuchEntityException)
            {
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete instance profile");
            }

            try
            {
                await _iamClient.DetachRolePolicyAsync(new DetachRolePolicyRequest
                {
                    RoleName = _roleName,
                    PolicyArn = AwsConfig.IamPolicyArn
                });
                _logger.Debug("Detached policy from role");
            }
            catch (NoSuchEntityException)
            {
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to detach policy from role");
            }

            try
            {
                await _iamClient.DeleteRoleAsync(new DeleteRoleRequest
                {
                    RoleName = _roleName
                });
                _logger.Debug("Deleted role {RoleName}", _roleName);
            }
            catch (NoSuchEntityException)
            {
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete role");
            }

            _logger.Information("IAM cleanup complete");
        }
    }
}
