using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon.EC2.Model;
using Serilog;

namespace Nexum.Tests.E2E.Orchestration
{
    public class Ec2Orchestrator : IDisposable
    {
        private readonly AmazonEC2Client _ec2Client;
        private readonly List<string> _instanceIds = new List<string>();
        private readonly string _instanceProfileArn;
        private readonly ILogger _logger;

        private string _securityGroupId;

        public Ec2Orchestrator(string instanceProfileArn)
        {
            _logger = Log.ForContext<Ec2Orchestrator>();
            _ec2Client = new AmazonEC2Client();
            _instanceProfileArn = instanceProfileArn;
        }

        public List<Ec2Instance> Instances { get; } = new List<Ec2Instance>();

        public void Dispose()
        {
            _ec2Client?.Dispose();
        }

        public async Task<string> CreateSecurityGroupAsync()
        {
            _logger.Information("Creating security group {GroupName}", AwsConfig.SecurityGroupName);

            try
            {
                var describeResponse = await _ec2Client.DescribeSecurityGroupsAsync(new DescribeSecurityGroupsRequest
                {
                    Filters =
                    [
                        new Filter { Name = "group-name", Values = [AwsConfig.SecurityGroupName] }
                    ]
                });

                if (describeResponse.SecurityGroups.Count > 0)
                {
                    _securityGroupId = describeResponse.SecurityGroups[0].GroupId;
                    _logger.Information("Security group already exists: {GroupId}", _securityGroupId);
                    return _securityGroupId;
                }
            }
            catch (Exception)
            {
            }

            var vpcResponse = await _ec2Client.DescribeVpcsAsync(new DescribeVpcsRequest
            {
                Filters = [new Filter { Name = "isDefault", Values = ["true"] }]
            });

            string vpcId = vpcResponse.Vpcs.FirstOrDefault()?.VpcId;
            if (string.IsNullOrEmpty(vpcId))
                throw new InvalidOperationException("No default VPC found");

            var createResponse = await _ec2Client.CreateSecurityGroupAsync(new CreateSecurityGroupRequest
            {
                GroupName = AwsConfig.SecurityGroupName,
                Description = "Security group for Nexum E2E tests",
                VpcId = vpcId,
                TagSpecifications =
                [
                    new TagSpecification
                    {
                        ResourceType = ResourceType.SecurityGroup,
                        Tags =
                        [
                            new Tag { Key = AwsConfig.ResourceTagKey, Value = AwsConfig.ResourceTagValue }
                        ]
                    }
                ]
            });

            _securityGroupId = createResponse.GroupId;
            _logger.Information("Created security group: {GroupId}", _securityGroupId);

            var ingressRequest = new AuthorizeSecurityGroupIngressRequest
            {
                GroupId = _securityGroupId,
                IpPermissions =
                [
                    new IpPermission
                    {
                        IpProtocol = "tcp",
                        FromPort = AwsConfig.TcpPort,
                        ToPort = AwsConfig.TcpPort,
                        Ipv4Ranges = [new IpRange { CidrIp = "0.0.0.0/0" }]
                    },
                    new IpPermission
                    {
                        IpProtocol = "udp",
                        FromPort = AwsConfig.UdpPorts.Min(),
                        ToPort = AwsConfig.UdpPorts.Max(),
                        Ipv4Ranges = [new IpRange { CidrIp = "0.0.0.0/0" }]
                    },
                    new IpPermission
                    {
                        IpProtocol = "-1",
                        UserIdGroupPairs = [new UserIdGroupPair { GroupId = _securityGroupId }]
                    }
                ]
            };

            await _ec2Client.AuthorizeSecurityGroupIngressAsync(ingressRequest);
            _logger.Information("Added security group rules for TCP {TcpPort} and UDP {UdpPorts}",
                AwsConfig.TcpPort, string.Join(",", AwsConfig.UdpPorts));

            return _securityGroupId;
        }

        public async Task<Ec2Instance> LaunchInstanceAsync(string name, string role)
        {
            _logger.Information("Launching EC2 instance {Name} ({Role})", name, role);

            string amiId = await GetLatestAmazonLinux2023AmiAsync();

            var runRequest = new RunInstancesRequest
            {
                ImageId = amiId,
                InstanceType = AwsConfig.InstanceType,
                MinCount = 1,
                MaxCount = 1,
                SecurityGroupIds = [_securityGroupId],
                IamInstanceProfile = new IamInstanceProfileSpecification
                {
                    Arn = _instanceProfileArn
                },
                TagSpecifications =
                [
                    new TagSpecification
                    {
                        ResourceType = ResourceType.Instance,
                        Tags =
                        [
                            new Tag { Key = "Name", Value = name },
                            new Tag { Key = AwsConfig.ResourceTagKey, Value = AwsConfig.ResourceTagValue },
                            new Tag { Key = "Role", Value = role }
                        ]
                    }
                ]
            };

            var response = await _ec2Client.RunInstancesAsync(runRequest);
            var instance = response.Reservation.Instances[0];
            string instanceId = instance.InstanceId;

            _instanceIds.Add(instanceId);
            _logger.Information("Launched instance {InstanceId}", instanceId);

            await WaitForInstanceStateAsync(instanceId, InstanceStateName.Running);

            var describeResponse = await _ec2Client.DescribeInstancesAsync(new DescribeInstancesRequest
            {
                InstanceIds = [instanceId]
            });

            var runningInstance = describeResponse.Reservations[0].Instances[0];
            string publicIp = runningInstance.PublicIpAddress;

            _logger.Information("Instance {InstanceId} running with public IP {PublicIp}", instanceId, publicIp);

            var ec2Instance = new Ec2Instance
            {
                InstanceId = instanceId,
                PublicIp = publicIp,
                Name = name,
                Role = role
            };

            Instances.Add(ec2Instance);
            return ec2Instance;
        }

        private async Task<string> GetLatestAmazonLinux2023AmiAsync()
        {
            var response = await _ec2Client.DescribeImagesAsync(new DescribeImagesRequest
            {
                Owners = ["amazon"],
                Filters =
                [
                    new Filter { Name = "name", Values = ["al2023-ami-*-arm64"] },
                    new Filter { Name = "state", Values = ["available"] },
                    new Filter { Name = "architecture", Values = ["arm64"] }
                ]
            });


            var images = response.Images ?? new List<Image>();

            var standardCandidates = images
                .Where(IsStandardAmazonLinux2023Ami)
                .Select(i => new
                {
                    Image = i,
                    Kernel = TryParseKernelVersion(i.Name),
                    Created = TryParseCreationDate(i.CreationDate)
                })
                .ToList();

            var bestStandard = standardCandidates
                .OrderByDescending(x => x.Kernel ?? new Version(0, 0))
                .ThenByDescending(x => x.Created ?? DateTimeOffset.MinValue)
                .FirstOrDefault();

            var bestNonMinimal = images
                .Where(i => !IsMinimalAmazonLinux2023Ami(i))
                .Select(i => new
                {
                    Image = i,
                    Kernel = TryParseKernelVersion(i.Name),
                    Created = TryParseCreationDate(i.CreationDate)
                })
                .OrderByDescending(x => x.Kernel ?? new Version(0, 0))
                .ThenByDescending(x => x.Created ?? DateTimeOffset.MinValue)
                .FirstOrDefault();

            var bestAny = images
                .Select(i => new
                {
                    Image = i,
                    Kernel = TryParseKernelVersion(i.Name),
                    Created = TryParseCreationDate(i.CreationDate)
                })
                .OrderByDescending(x => x.Kernel ?? new Version(0, 0))
                .ThenByDescending(x => x.Created ?? DateTimeOffset.MinValue)
                .FirstOrDefault();

            var selected = bestStandard ?? bestNonMinimal ?? bestAny;

            if (selected?.Image == null)
                throw new InvalidOperationException("No Amazon Linux 2023 ARM64 AMI found");

            _logger.Information(
                "Selected AL2023 AMI {AmiId} ({Name}) Kernel={Kernel} Created={Created} [standardCandidates={StandardCount}, totalImages={TotalCount}]",
                selected.Image.ImageId,
                selected.Image.Name,
                selected.Kernel?.ToString() ?? "unknown",
                selected.Created?.ToString("u") ?? "unknown",
                standardCandidates.Count,
                images.Count);

            return selected.Image.ImageId;
        }

        private static bool IsStandardAmazonLinux2023Ami(Image image)
        {
            string name = image?.Name ?? string.Empty;
            if (!name.StartsWith("al2023-ami-", StringComparison.OrdinalIgnoreCase))
                return false;

            if (IsMinimalAmazonLinux2023Ami(image))
                return false;

            if (name.Contains("-ecs-", StringComparison.OrdinalIgnoreCase))
                return false;
            if (name.Contains("-nvidia-", StringComparison.OrdinalIgnoreCase))
                return false;
            if (name.Contains("-sap-", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!name.EndsWith("-arm64", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!name.Contains("-kernel-", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static bool IsMinimalAmazonLinux2023Ami(Image image)
        {
            string name = image?.Name ?? string.Empty;
            return name.Contains("-minimal-", StringComparison.OrdinalIgnoreCase);
        }

        private static Version TryParseKernelVersion(string amiName)
        {
            if (string.IsNullOrWhiteSpace(amiName))
                return null;

            var m = Regex.Match(amiName, @"-kernel-(?<ver>\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (!m.Success)
                return null;

            string[] parts = m.Groups["ver"].Value.Split('.');
            if (parts.Length < 2)
                return null;

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major))
                return null;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
                return null;

            if (parts.Length >= 3 &&
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
                return new Version(major, minor, patch);

            return new Version(major, minor);
        }

        private static DateTimeOffset? TryParseCreationDate(string creationDate)
        {
            if (string.IsNullOrWhiteSpace(creationDate))
                return null;

            if (DateTimeOffset.TryParse(creationDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
                return dto;

            return null;
        }

        private async Task WaitForInstanceStateAsync(string instanceId, InstanceStateName targetState)
        {
            _logger.Information("Waiting for instance {InstanceId} to reach state {TargetState}",
                instanceId, targetState);

            var timeout = DateTime.UtcNow.AddMinutes(5);
            while (DateTime.UtcNow < timeout)
            {
                try
                {
                    var response = await _ec2Client.DescribeInstancesAsync(new DescribeInstancesRequest
                    {
                        InstanceIds = [instanceId]
                    });

                    if (response.Reservations.Count > 0 && response.Reservations[0].Instances.Count > 0)
                    {
                        var instance = response.Reservations[0].Instances[0];
                        if (instance.State.Name == targetState)
                        {
                            _logger.Information("Instance {InstanceId} is now {State}", instanceId, targetState);
                            return;
                        }
                    }
                }
                catch (AmazonEC2Exception ex) when (ex.ErrorCode == "InvalidInstanceID.NotFound")
                {
                    _logger.Debug("Instance {InstanceId} not yet visible, retrying...", instanceId);
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            throw new TimeoutException($"Instance {instanceId} did not reach state {targetState} within timeout");
        }

        public async Task TerminateAllInstancesAsync()
        {
            if (_instanceIds.Count == 0)
            {
                _logger.Information("No instances to terminate");
                return;
            }

            _logger.Information("Terminating {Count} instances", _instanceIds.Count);

            await _ec2Client.TerminateInstancesAsync(new TerminateInstancesRequest
            {
                InstanceIds = _instanceIds
            });

            foreach (string instanceId in _instanceIds)
                try
                {
                    await WaitForInstanceStateAsync(instanceId, InstanceStateName.Terminated);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error waiting for instance {InstanceId} to terminate", instanceId);
                }

            _logger.Information("All instances terminated");
        }

        public async Task DeleteSecurityGroupAsync()
        {
            if (string.IsNullOrEmpty(_securityGroupId))
                return;

            _logger.Information("Deleting security group {GroupId}", _securityGroupId);

            for (int i = 0; i < 5; i++)
                try
                {
                    await _ec2Client.DeleteSecurityGroupAsync(new DeleteSecurityGroupRequest
                    {
                        GroupId = _securityGroupId
                    });
                    _logger.Information("Security group deleted");
                    return;
                }
                catch (AmazonEC2Exception ex) when (ex.ErrorCode == "DependencyViolation")
                {
                    _logger.Debug("Security group still in use, retrying in 10 seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }

            _logger.Warning("Could not delete security group after retries");
        }
    }

    public class Ec2Instance
    {
        public string InstanceId { get; set; }
        public string PublicIp { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
    }
}
