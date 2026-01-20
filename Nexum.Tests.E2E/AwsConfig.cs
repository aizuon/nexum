namespace Nexum.Tests.E2E
{
    public static class AwsConfig
    {
        public const string S3BucketName = "nexum-e2e-artifacts";
        public const string IamRoleName = "nexum-e2e-instance-role";
        public const string IamPolicyArn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore";
        public const string InstanceProfileName = "nexum-e2e-instance-profile";
        public const string SecurityGroupName = "nexum-e2e-sg";
        public const string ResourceTagKey = "nexum-e2e";
        public const string ResourceTagValue = "true";

        public const int TcpPort = 28000;

        public const string InstanceType = "t4g.micro";

        public const string ServerBinaryKey = "e2e-server/Nexum.E2E.Server";
        public const string ClientBinaryKey = "e2e-client/Nexum.E2E.Client";
        public static readonly int[] UdpPorts = { 29000, 29001, 29002, 29003 };
    }
}
