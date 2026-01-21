using System.Collections.Concurrent;
using System.Collections.Generic;
using Nexum.Core;

namespace Nexum.Server
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
                {
                    var p2pGroupMemberJoin = new NetMessage();
                    p2pGroupMemberJoin.Write(HostId);
                    p2pGroupMemberJoin.Write(session.HostId);
                    p2pGroupMemberJoin.Write(new ByteArray());
                    p2pGroupMemberJoin.Write(0);
                    p2pGroupMemberJoin.Write(new ByteArray(crypt.GetKey(), true));
                    p2pGroupMemberJoin.Write(new ByteArray(crypt.GetFastKey(), true));
                    p2pGroupMemberJoin.Write(memberToJoin.P2PFirstFrameNumber);
                    p2pGroupMemberJoin.Write(memberToJoin.ConnectionMagicNumber);
                    p2pGroupMemberJoin.Write(Server.AllowDirectP2P);
                    p2pGroupMemberJoin.Write(session.UdpEndPoint?.Port ?? 0);

                    session.RmiToClient((ushort)NexumOpCode.P2PGroup_MemberJoin, p2pGroupMemberJoin);
                }
                else
                {
                    var p2pGroupMemberJoin = new NetMessage();
                    p2pGroupMemberJoin.Write(HostId);
                    p2pGroupMemberJoin.Write(session.HostId);
                    p2pGroupMemberJoin.Write(new ByteArray());
                    p2pGroupMemberJoin.Write(0);
                    p2pGroupMemberJoin.Write(memberToJoin.P2PFirstFrameNumber);
                    p2pGroupMemberJoin.Write(memberToJoin.ConnectionMagicNumber);
                    p2pGroupMemberJoin.Write(Server.AllowDirectP2P);
                    p2pGroupMemberJoin.Write(session.UdpEndPoint?.Port ?? 0);

                    session.RmiToClient((ushort)NexumOpCode.P2PGroup_MemberJoin_Unencrypted, p2pGroupMemberJoin);
                }

                foreach (var member in P2PMembersInternal.Values)
                {
                    if (member.Session.HostId == session.HostId)
                        continue;
                    member.Session.Logger.Debug(
                        "Notifying Client({ExistingHostId}) about new member Client({NewHostId}) in P2PGroup({GroupHostId})",
                        member.Session.HostId, session.HostId, HostId);

                    var stateA = new P2PConnectionState(member);
                    var stateB = new P2PConnectionState(memberToJoin);

                    memberToJoin.ConnectionStates[member.Session.HostId] = stateA;
                    member.ConnectionStates[session.HostId] = stateB;

                    if (encrypted)
                    {
                        var p2pGroupMemberJoin1 = new NetMessage();
                        p2pGroupMemberJoin1.Write(HostId);
                        p2pGroupMemberJoin1.Write(session.HostId);
                        p2pGroupMemberJoin1.Write(new ByteArray());
                        p2pGroupMemberJoin1.Write(stateB.EventId);
                        p2pGroupMemberJoin1.Write(new ByteArray(crypt.GetKey(), true));
                        p2pGroupMemberJoin1.Write(new ByteArray(crypt.GetFastKey(), true));
                        p2pGroupMemberJoin1.Write(memberToJoin.P2PFirstFrameNumber);
                        p2pGroupMemberJoin1.Write(memberToJoin.ConnectionMagicNumber);
                        p2pGroupMemberJoin1.Write(Server.AllowDirectP2P);
                        p2pGroupMemberJoin1.Write(session.UdpEndPoint?.Port ?? 0);

                        member.Session.RmiToClient((ushort)NexumOpCode.P2PGroup_MemberJoin, p2pGroupMemberJoin1);

                        var p2pGroupMemberJoin2 = new NetMessage();
                        p2pGroupMemberJoin2.Write(HostId);
                        p2pGroupMemberJoin2.Write(member.Session.HostId);
                        p2pGroupMemberJoin2.Write(new ByteArray());
                        p2pGroupMemberJoin2.Write(stateA.EventId);
                        p2pGroupMemberJoin2.Write(new ByteArray(crypt.GetKey(), true));
                        p2pGroupMemberJoin2.Write(new ByteArray(crypt.GetFastKey(), true));
                        p2pGroupMemberJoin2.Write(member.P2PFirstFrameNumber);
                        p2pGroupMemberJoin2.Write(member.ConnectionMagicNumber);
                        p2pGroupMemberJoin2.Write(Server.AllowDirectP2P);
                        p2pGroupMemberJoin2.Write(member.Session.UdpEndPoint?.Port ?? 0);

                        session.RmiToClient((ushort)NexumOpCode.P2PGroup_MemberJoin, p2pGroupMemberJoin2);
                    }
                    else
                    {
                        var p2pGroupMemberJoin1 = new NetMessage();
                        p2pGroupMemberJoin1.Write(HostId);
                        p2pGroupMemberJoin1.Write(session.HostId);
                        p2pGroupMemberJoin1.Write(new ByteArray());
                        p2pGroupMemberJoin1.Write(stateB.EventId);
                        p2pGroupMemberJoin1.Write(memberToJoin.P2PFirstFrameNumber);
                        p2pGroupMemberJoin1.Write(memberToJoin.ConnectionMagicNumber);
                        p2pGroupMemberJoin1.Write(Server.AllowDirectP2P);
                        p2pGroupMemberJoin1.Write(session.UdpEndPoint?.Port ?? 0);

                        member.Session.RmiToClient((ushort)NexumOpCode.P2PGroup_MemberJoin_Unencrypted,
                            p2pGroupMemberJoin1);

                        var p2pGroupMemberJoin2 = new NetMessage();
                        p2pGroupMemberJoin2.Write(HostId);
                        p2pGroupMemberJoin2.Write(member.Session.HostId);
                        p2pGroupMemberJoin2.Write(new ByteArray());
                        p2pGroupMemberJoin2.Write(stateA.EventId);
                        p2pGroupMemberJoin2.Write(member.P2PFirstFrameNumber);
                        p2pGroupMemberJoin2.Write(member.ConnectionMagicNumber);
                        p2pGroupMemberJoin2.Write(Server.AllowDirectP2P);
                        p2pGroupMemberJoin2.Write(member.Session.UdpEndPoint?.Port ?? 0);

                        session.RmiToClient((ushort)NexumOpCode.P2PGroup_MemberJoin_Unencrypted, p2pGroupMemberJoin2);
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

                var p2pGroupMemberLeave = new NetMessage();
                p2pGroupMemberLeave.Write(HostId);
                p2pGroupMemberLeave.Write(session.HostId);

                session.RmiToClient((ushort)NexumOpCode.P2PGroup_MemberLeave, p2pGroupMemberLeave);

                memberToLeave.ConnectionStates.Clear();

                foreach (var member in P2PMembersInternal.Values)
                {
                    if (member.Session.HostId == session.HostId)
                        continue;

                    var p2pGroupMemberLeave2 = new NetMessage();
                    p2pGroupMemberLeave2.Write(session.HostId);
                    p2pGroupMemberLeave2.Write(HostId);

                    member.Session.RmiToClient((ushort)NexumOpCode.P2PGroup_MemberLeave, p2pGroupMemberLeave2);

                    var p2pGroupMemberLeave3 = new NetMessage();
                    p2pGroupMemberLeave3.Write(member.Session.HostId);
                    p2pGroupMemberLeave3.Write(HostId);

                    session.RmiToClient((ushort)NexumOpCode.P2PGroup_MemberLeave, p2pGroupMemberLeave3);

                    member.ConnectionStates.TryRemove(session.HostId, out _);
                }
            }
        }
    }
}
