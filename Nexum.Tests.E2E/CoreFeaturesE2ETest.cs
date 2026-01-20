using System;
using System.Threading.Tasks;
using Nexum.Tests.E2E.Orchestration;
using Serilog;
using Xunit;
using Xunit.Abstractions;

namespace Nexum.Tests.E2E
{
    [Collection("E2E")]
    public class CoreFeaturesE2ETest : IAsyncLifetime
    {
        private readonly ILogger _logger;
        private readonly ITestOutputHelper _output;
        private Ec2Instance _client1Instance;
        private Ec2Instance _client2Instance;
        private Ec2Orchestrator _ec2Orchestrator;
        private IamProvisioner _iamProvisioner;

        private Ec2Instance _serverInstance;
        private SsmCommandRunner _ssmRunner;

        public CoreFeaturesE2ETest(ITestOutputHelper output)
        {
            _output = output;

            Log.Logger = new LoggerConfiguration()
                .WriteTo.TestOutput(output)
                .MinimumLevel.Debug()
                .CreateLogger();

            _logger = Log.ForContext<CoreFeaturesE2ETest>();
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            _logger.Information("Starting cleanup...");

            try
            {
                if (_ec2Orchestrator != null)
                {
                    await _ec2Orchestrator.TerminateAllInstancesAsync();
                    await _ec2Orchestrator.DeleteSecurityGroupAsync();
                    _ec2Orchestrator.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error during EC2 cleanup");
            }

            try
            {
                if (_iamProvisioner != null)
                {
                    await _iamProvisioner.CleanupAsync();
                    _iamProvisioner.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error during IAM cleanup");
            }

            _ssmRunner?.Dispose();

            _logger.Information("Cleanup complete");
        }

        [Fact(Timeout = 600000)]
        public async Task AllCoreFeatures_OnSeparateEc2Instances_Pass()
        {
            _logger.Information("=== Phase 1: Provisioning AWS Resources ===");

            _iamProvisioner = new IamProvisioner();
            string instanceProfileArn = await _iamProvisioner.ProvisionAsync();

            _ec2Orchestrator = new Ec2Orchestrator(instanceProfileArn);
            await _ec2Orchestrator.CreateSecurityGroupAsync();

            _logger.Information("=== Phase 2: Launching EC2 Instances ===");

            var serverTask = _ec2Orchestrator.LaunchInstanceAsync("nexum-e2e-server", "server");
            var client1Task = _ec2Orchestrator.LaunchInstanceAsync("nexum-e2e-client-1", "client");
            var client2Task = _ec2Orchestrator.LaunchInstanceAsync("nexum-e2e-client-2", "client");

            await Task.WhenAll(serverTask, client1Task, client2Task);

            _serverInstance = await serverTask;
            _client1Instance = await client1Task;
            _client2Instance = await client2Task;

            _logger.Information("Server: {Ip}, Client1: {Ip1}, Client2: {Ip2}",
                _serverInstance.PublicIp, _client1Instance.PublicIp, _client2Instance.PublicIp);

            _logger.Information("=== Phase 3: Waiting for SSM Agents ===");

            _ssmRunner = new SsmCommandRunner();
            var ssmTimeout = TimeSpan.FromMinutes(3);

            await Task.WhenAll(
                _ssmRunner.WaitForSsmOnlineAsync(_serverInstance.InstanceId, ssmTimeout),
                _ssmRunner.WaitForSsmOnlineAsync(_client1Instance.InstanceId, ssmTimeout),
                _ssmRunner.WaitForSsmOnlineAsync(_client2Instance.InstanceId, ssmTimeout)
            );

            _logger.Information("=== Phase 4: Installing .NET Runtime and Binaries ===");

            string setupCommand = GetSetupCommand();

            var setupTasks = new[]
            {
                _ssmRunner.RunCommandAsync(_serverInstance.InstanceId, setupCommand, TimeSpan.FromMinutes(5)),
                _ssmRunner.RunCommandAsync(_client1Instance.InstanceId, setupCommand, TimeSpan.FromMinutes(5)),
                _ssmRunner.RunCommandAsync(_client2Instance.InstanceId, setupCommand, TimeSpan.FromMinutes(5))
            };

            var setupResults = await Task.WhenAll(setupTasks);

            foreach (var result in setupResults)
                Assert.True(result.Success, $"Setup failed: {result.StandardError}");

            _logger.Information("=== Phase 5: Starting E2E Server ===");

            string serverCommand =
                $"cd /tmp/e2e && chmod +x ./Nexum.E2E.Server && ./Nexum.E2E.Server --bind-ip 0.0.0.0 --tcp-port {AwsConfig.TcpPort} --udp-ports {string.Join(",", AwsConfig.UdpPorts)}";
            await _ssmRunner.StartBackgroundCommandAsync(_serverInstance.InstanceId, serverCommand);

            _logger.Information("Waiting for server ports to become available...");
            await _ssmRunner.WaitForPortAsync(_client1Instance.InstanceId, _serverInstance.PublicIp, AwsConfig.TcpPort,
                "tcp", TimeSpan.FromSeconds(60));
            _logger.Information("Server TCP port is ready");

            _logger.Information("=== Phase 6: Running E2E Clients ===");

            string client1Command =
                $"cd /tmp/e2e && chmod +x ./Nexum.E2E.Client && ./Nexum.E2E.Client --server-host {_serverInstance.PublicIp} --tcp-port {AwsConfig.TcpPort} --client-id 1";
            string client2Command =
                $"cd /tmp/e2e && chmod +x ./Nexum.E2E.Client && ./Nexum.E2E.Client --server-host {_serverInstance.PublicIp} --tcp-port {AwsConfig.TcpPort} --client-id 2";

            var clientTimeout = TimeSpan.FromMinutes(5);
            var client1Task2 = _ssmRunner.RunCommandAsync(_client1Instance.InstanceId, client1Command, clientTimeout);
            var client2Task2 = _ssmRunner.RunCommandAsync(_client2Instance.InstanceId, client2Command, clientTimeout);

            var clientResults = await Task.WhenAll(client1Task2, client2Task2);

            _logger.Information("=== Phase 7: Verifying Results ===");

            var client1Result = clientResults[0];
            var client2Result = clientResults[1];

            _logger.Information("Client 1 output:\n{Output}", client1Result.StandardOutput);
            _logger.Information("Client 2 output:\n{Output}", client2Result.StandardOutput);

            if (!string.IsNullOrEmpty(client1Result.StandardError))
                _logger.Warning("Client 1 stderr:\n{Error}", client1Result.StandardError);
            if (!string.IsNullOrEmpty(client2Result.StandardError))
                _logger.Warning("Client 2 stderr:\n{Error}", client2Result.StandardError);

            Assert.True(client1Result.Success && client1Result.ExitCode == 0,
                $"Client 1 failed with exit code {client1Result.ExitCode}:\n{client1Result.StandardError}");
            Assert.True(client2Result.Success && client2Result.ExitCode == 0,
                $"Client 2 failed with exit code {client2Result.ExitCode}:\n{client2Result.StandardError}");

            _logger.Information("=== ALL E2E TESTS PASSED ===");
        }

        private static string GetSetupCommand()
        {
            return $@"
set -e

# Create directory and download binaries
mkdir -p /tmp/e2e
cd /tmp/e2e

echo 'Downloading server binary...'
aws s3 cp s3://{AwsConfig.S3BucketName}/{AwsConfig.ServerBinaryKey} ./Nexum.E2E.Server

echo 'Downloading client binary...'
aws s3 cp s3://{AwsConfig.S3BucketName}/{AwsConfig.ClientBinaryKey} ./Nexum.E2E.Client

chmod +x ./Nexum.E2E.Server ./Nexum.E2E.Client

echo 'Setup complete'
";
        }
    }
}
