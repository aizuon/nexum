using System.Collections.Concurrent;
using System.Collections.Generic;
using Nexum.Core.Crypto;
using Nexum.Core.Rmi.S2C;
using Nexum.Core.Serialization;
using Nexum.Server.Core;
using Nexum.Server.Sessions;

namespace Nexum.Server.P2P
{
    public class P2PGroup
    {
        internal P2PGroup(NetServer server)
        {
            Server = server;
            HostId = Server.HostIdFactory.New();
        }

        public uint HostId { get; }

        public IReadOnlyDictionary<uint, P2PMember> P2PMembers => P2PMembersInternal;

        internal ConcurrentDictionary<uint, P2PMember> P2PMembersInternal { get; } =
            new ConcurrentDictionary<uint, P2PMember>();

        internal NetServer Server { get; }

        public void Join(NetSession session)
        {
            bool encrypted = Server.NetSettings.EnableP2PEncryptedMessaging;
            NetCrypt crypt = null;
            if (encrypted)
                crypt = new NetCrypt(Server.NetSettings.EncryptedMessageKeyLength,
                    Server.NetSettings.FastEncryptedMessageKeyLength);

            var memberToJoin = new P2PMember(HostId, session);
            if (P2PMembersInternal.TryAdd(session.HostId, memberToJoin))
            {
                session.Logger.Debug(
                    "Client({HostId}) joined P2PGroup({GroupHostId}), memberCount = {MemberCount}",
                    session.HostId, HostId, P2PMembersInternal.Count);
                session.P2PGroup = this;
                Server.InitiateUdpSetup(session);

                if (encrypted)
                    session.RmiToClient(new P2PGroupMemberJoin
                    {
                        GroupHostId = HostId,
                        HostId = session.HostId,
                        CustomField = new ByteArray(),
                        EventId = 0,
                        SessionKey = new ByteArray(crypt.GetKey(), true),
                        FastSessionKey = new ByteArray(crypt.GetFastKey(), true),
                        P2PFirstFrameNumber = memberToJoin.P2PFirstFrameNumber,
                        PeerUdpMagicNumber = memberToJoin.ConnectionMagicNumber,
                        EnableDirectP2P = Server.AllowDirectP2P,
                        BindPort = session.UdpLocalEndPoint?.Port ?? 0
                    });
                else
                    session.RmiToClient(new P2PGroupMemberJoinUnencrypted
                    {
                        GroupHostId = HostId,
                        HostId = session.HostId,
                        CustomField = new ByteArray(),
                        EventId = 0,
                        P2PFirstFrameNumber = memberToJoin.P2PFirstFrameNumber,
                        PeerUdpMagicNumber = memberToJoin.ConnectionMagicNumber,
                        EnableDirectP2P = Server.AllowDirectP2P,
                        BindPort = session.UdpLocalEndPoint?.Port ?? 0
                    });

                foreach (var member in P2PMembersInternal.Values)
                {
                    if (member.Session.HostId == session.HostId)
                        continue;
                    member.Session.Logger.Debug(
                        "Notifying Client({ExistingHostId}) about new member Client({NewHostId}) in P2PGroup({GroupHostId})",
                        member.Session.HostId, session.HostId, HostId);

                    int existingPortForMember = 0;
                    int existingPortForJoiner = 0;
                    if (member.ConnectionStates.TryGetValue(session.HostId, out var existingStateForMember))
                        existingPortForMember = existingStateForMember.LastSuccessfulLocalPort;
                    else if (member.Session.LastSuccessfulP2PLocalPorts.TryGetValue(session.HostId,
                                 out int preservedPortForMember))
                        existingPortForMember = preservedPortForMember;
                    if (memberToJoin.ConnectionStates.TryGetValue(member.Session.HostId,
                            out var existingStateForJoiner))
                        existingPortForJoiner = existingStateForJoiner.LastSuccessfulLocalPort;
                    else if (session.LastSuccessfulP2PLocalPorts.TryGetValue(member.Session.HostId,
                                 out int preservedPortForJoiner))
                        existingPortForJoiner = preservedPortForJoiner;

                    var stateA = new P2PConnectionState(member) { LastSuccessfulLocalPort = existingPortForJoiner };
                    var stateB = new P2PConnectionState(memberToJoin)
                        { LastSuccessfulLocalPort = existingPortForMember };

                    int bindPortForMember = existingPortForMember > 0
                        ? existingPortForMember
                        : GetAnyActiveP2PLocalPort(member)
                          ?? member.Session.UdpLocalEndPoint?.Port ?? 0;
                    int bindPortForJoiner = existingPortForJoiner > 0
                        ? existingPortForJoiner
                        : GetAnyActiveP2PLocalPort(memberToJoin)
                          ?? session.UdpLocalEndPoint?.Port ?? 0;

                    memberToJoin.ConnectionStates[member.Session.HostId] = stateA;
                    member.ConnectionStates[session.HostId] = stateB;

                    if (encrypted)
                    {
                        member.Session.RmiToClient(new P2PGroupMemberJoin
                        {
                            GroupHostId = HostId,
                            HostId = session.HostId,
                            CustomField = new ByteArray(),
                            EventId = stateB.EventId,
                            SessionKey = new ByteArray(crypt.GetKey(), true),
                            FastSessionKey = new ByteArray(crypt.GetFastKey(), true),
                            P2PFirstFrameNumber = memberToJoin.P2PFirstFrameNumber,
                            PeerUdpMagicNumber = memberToJoin.ConnectionMagicNumber,
                            EnableDirectP2P = Server.AllowDirectP2P,
                            BindPort = bindPortForMember
                        });

                        session.RmiToClient(new P2PGroupMemberJoin
                        {
                            GroupHostId = HostId,
                            HostId = member.Session.HostId,
                            CustomField = new ByteArray(),
                            EventId = stateA.EventId,
                            SessionKey = new ByteArray(crypt.GetKey(), true),
                            FastSessionKey = new ByteArray(crypt.GetFastKey(), true),
                            P2PFirstFrameNumber = member.P2PFirstFrameNumber,
                            PeerUdpMagicNumber = member.ConnectionMagicNumber,
                            EnableDirectP2P = Server.AllowDirectP2P,
                            BindPort = bindPortForJoiner
                        });
                    }
                    else
                    {
                        member.Session.RmiToClient(new P2PGroupMemberJoinUnencrypted
                        {
                            GroupHostId = HostId,
                            HostId = session.HostId,
                            CustomField = new ByteArray(),
                            EventId = stateB.EventId,
                            P2PFirstFrameNumber = memberToJoin.P2PFirstFrameNumber,
                            PeerUdpMagicNumber = memberToJoin.ConnectionMagicNumber,
                            EnableDirectP2P = Server.AllowDirectP2P,
                            BindPort = bindPortForMember
                        });

                        session.RmiToClient(new P2PGroupMemberJoinUnencrypted
                        {
                            GroupHostId = HostId,
                            HostId = member.Session.HostId,
                            CustomField = new ByteArray(),
                            EventId = stateA.EventId,
                            P2PFirstFrameNumber = member.P2PFirstFrameNumber,
                            PeerUdpMagicNumber = member.ConnectionMagicNumber,
                            EnableDirectP2P = Server.AllowDirectP2P,
                            BindPort = bindPortForJoiner
                        });
                    }
                }
            }
        }

        public void Leave(NetSession session)
        {
            if (P2PMembersInternal.TryRemove(session.HostId, out var memberToLeave))
            {
                session.Logger.Debug(
                    "Client({HostId}) left P2PGroup({GroupHostId}), memberCount = {MemberCount}",
                    session.HostId, HostId, P2PMembersInternal.Count);

                session.P2PGroup = null;

                session.RmiToClient(new P2PGroupMemberLeave
                {
                    HostId = HostId,
                    GroupHostId = session.HostId
                });

                foreach (var kvp in memberToLeave.ConnectionStates)
                {
                    uint remoteHostId = kvp.Key;
                    var state = kvp.Value;
                    if (state?.LastSuccessfulLocalPort > 0)
                        session.LastSuccessfulP2PLocalPorts[remoteHostId] = state.LastSuccessfulLocalPort;
                }

                memberToLeave.ConnectionStates.Clear();

                foreach (var member in P2PMembersInternal.Values)
                {
                    if (member.Session.HostId == session.HostId)
                        continue;

                    member.Session.RmiToClient(new P2PGroupMemberLeave
                    {
                        HostId = session.HostId,
                        GroupHostId = HostId
                    });

                    session.RmiToClient(new P2PGroupMemberLeave
                    {
                        HostId = member.Session.HostId,
                        GroupHostId = HostId
                    });

                    if (member.ConnectionStates.TryRemove(session.HostId, out var removedState) &&
                        removedState.LastSuccessfulLocalPort > 0)
                        member.Session.LastSuccessfulP2PLocalPorts[session.HostId] =
                            removedState.LastSuccessfulLocalPort;
                }
            }
        }

        private static int? GetAnyActiveP2PLocalPort(P2PMember member)
        {
            foreach (var state in member.ConnectionStates.Values)
                if (state.HolepunchSuccess && state.LocalEndPoint?.Port > 0)
                    return state.LocalEndPoint.Port;

            return null;
        }
    }
}
