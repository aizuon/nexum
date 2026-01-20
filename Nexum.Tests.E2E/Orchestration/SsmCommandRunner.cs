using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Serilog;

namespace Nexum.Tests.E2E.Orchestration
{
    public class SsmCommandRunner : IDisposable
    {
        private readonly ILogger _logger;
        private readonly AmazonSimpleSystemsManagementClient _ssmClient;

        public SsmCommandRunner()
        {
            _logger = Log.ForContext<SsmCommandRunner>();
            _ssmClient = new AmazonSimpleSystemsManagementClient();
        }

        public void Dispose()
        {
            _ssmClient?.Dispose();
        }

        public async Task WaitForSsmOnlineAsync(string instanceId, TimeSpan timeout)
        {
            _logger.Information("Waiting for SSM agent to come online on {InstanceId}", instanceId);

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var response = await _ssmClient.DescribeInstanceInformationAsync(
                        new DescribeInstanceInformationRequest
                        {
                            Filters =
                            [
                                new InstanceInformationStringFilter
                                {
                                    Key = "InstanceIds",
                                    Values = [instanceId]
                                }
                            ]
                        });

                    if (response.InstanceInformationList.Count > 0 &&
                        response.InstanceInformationList[0].PingStatus == PingStatus.Online)
                    {
                        _logger.Information("SSM agent online on {InstanceId}", instanceId);
                        return;
                    }
                }
                catch
                {
                }

                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            throw new TimeoutException($"SSM agent did not come online on {instanceId} within timeout");
        }

        public async Task<CommandResult> RunCommandAsync(string instanceId, string command, TimeSpan timeout)
        {
            _logger.Information("Running command on {InstanceId}: {Command}", instanceId, command);

            var sendResponse = await _ssmClient.SendCommandAsync(new SendCommandRequest
            {
                InstanceIds = [instanceId],
                DocumentName = "AWS-RunShellScript",
                Parameters = new Dictionary<string, List<string>>
                {
                    ["commands"] = [command]
                },
                TimeoutSeconds = (int)timeout.TotalSeconds
            });

            string commandId = sendResponse.Command.CommandId;
            _logger.Debug("Command {CommandId} sent to {InstanceId}", commandId, instanceId);

            return await WaitForCommandCompletionAsync(commandId, instanceId, timeout);
        }

        public async Task<string> StartBackgroundCommandAsync(string instanceId, string command)
        {
            _logger.Information("Starting background command on {InstanceId}: {Command}", instanceId, command);

            string bgCommand =
                $"export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 && nohup bash -c '{command.Replace("'", "'\\''")}' > /tmp/e2e-output.log 2>&1 & echo $!";

            var sendResponse = await _ssmClient.SendCommandAsync(new SendCommandRequest
            {
                InstanceIds = [instanceId],
                DocumentName = "AWS-RunShellScript",
                Parameters = new Dictionary<string, List<string>>
                {
                    ["commands"] = [bgCommand]
                },
                TimeoutSeconds = 60
            });

            string commandId = sendResponse.Command.CommandId;
            var result = await WaitForCommandCompletionAsync(commandId, instanceId, TimeSpan.FromMinutes(1));

            _logger.Information("Background process started on {InstanceId}, PID: {Pid}",
                instanceId, result.StandardOutput?.Trim());

            return result.StandardOutput?.Trim();
        }

        public async Task WaitForPortAsync(string instanceId, string targetHost, int port, string protocol,
            TimeSpan timeout)
        {
            _logger.Information("Waiting for {Protocol} port {Port} on {Host} from {InstanceId}",
                protocol, port, targetHost, instanceId);

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                string checkCommand = protocol.ToLower() == "tcp"
                    ? $"timeout 5 bash -c '</dev/tcp/{targetHost}/{port}' && echo OPEN || echo CLOSED"
                    : $"timeout 5 bash -c 'echo -n | nc -u -w1 {targetHost} {port}' && echo OPEN || echo CLOSED";

                try
                {
                    var result = await RunCommandAsync(instanceId, checkCommand, TimeSpan.FromSeconds(30));
                    if (result.Success && result.StandardOutput?.Contains("OPEN") == true)
                    {
                        _logger.Information("{Protocol} port {Port} is open on {Host}", protocol, port, targetHost);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("Port check failed: {Message}", ex.Message);
                }

                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            throw new TimeoutException(
                $"{protocol} port {port} on {targetHost} did not become available within timeout");
        }

        private async Task<CommandResult> WaitForCommandCompletionAsync(string commandId, string instanceId,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
                try
                {
                    var response = await _ssmClient.GetCommandInvocationAsync(new GetCommandInvocationRequest
                    {
                        CommandId = commandId,
                        InstanceId = instanceId
                    });

                    switch (response.Status.Value)
                    {
                        case "Success":
                            _logger.Information("Command {CommandId} completed successfully", commandId);
                            return new CommandResult
                            {
                                Success = true,
                                ExitCode = response.ResponseCode,
                                StandardOutput = response.StandardOutputContent,
                                StandardError = response.StandardErrorContent
                            };

                        case "Failed":
                        case "TimedOut":
                        case "Cancelled":
                            _logger.Error("Command {CommandId} failed with status {Status}", commandId,
                                response.Status);
                            return new CommandResult
                            {
                                Success = false,
                                ExitCode = response.ResponseCode,
                                StandardOutput = response.StandardOutputContent,
                                StandardError = response.StandardErrorContent,
                                Status = response.Status.Value
                            };

                        case "Pending":
                        case "InProgress":
                        case "Delayed":
                            await Task.Delay(TimeSpan.FromSeconds(5));
                            break;
                    }
                }
                catch (InvocationDoesNotExistException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }

            throw new TimeoutException($"Command {commandId} did not complete within timeout");
        }
    }

    public class CommandResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public string Status { get; set; }
    }
}
