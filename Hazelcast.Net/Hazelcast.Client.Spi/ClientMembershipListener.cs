﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hazelcast.Client.Connection;
using Hazelcast.Client.Protocol;
using Hazelcast.Client.Protocol.Codec;
using Hazelcast.Core;
using Hazelcast.IO;
using Hazelcast.Logging;
using Hazelcast.Util;

namespace Hazelcast.Client.Spi
{
    internal class ClientMembershipListener
    {
        public const int InitialMembersTimeoutSeconds = 5;
        private static readonly ILogger Logger = Logging.Logger.GetLogger(typeof (ClientMembershipListener));
        private readonly HazelcastClient _client;
        private readonly ClientClusterService _clusterService;
        private readonly ClientConnectionManager _connectionManager;
        private readonly IList<IMember> _members = new List<IMember>();
        private readonly ClientPartitionService _partitionService;
        private ManualResetEventSlim _initialListFetched;

        public ClientMembershipListener(HazelcastClient client)
        {
            _client = client;
            _connectionManager = (ClientConnectionManager) client.GetConnectionManager();
            _partitionService = (ClientPartitionService) client.GetClientPartitionService();
            _clusterService = (ClientClusterService) client.GetClientClusterService();
        }

        public void HandleMember(IMember member, int eventType)
        {
            switch (eventType)
            {
                case MembershipEvent.MemberAdded:
                {
                    MemberAdded(member);
                    break;
                }

                case MembershipEvent.MemberRemoved:
                {
                    MemberRemoved(member);
                    break;
                }

                default:
                {
                    Logger.Warning("Unknown event type :" + eventType);
                    break;
                }
            }
            _partitionService.RefreshPartitions();
        }

        public void HandleMemberCollection(ICollection<IMember> initialMembers)
        {
            var prevMembers = new Dictionary<string, IMember>();
            if (_members.Any())
            {
                prevMembers = new Dictionary<string, IMember>(_members.Count);
                foreach (var member in _members)
                {
                    prevMembers[member.GetUuid()] = member;
                }
                _members.Clear();
            }
            foreach (var initialMember in initialMembers)
            {
                _members.Add(initialMember);
            }

            var events = DetectMembershipEvents(prevMembers);
            if (events.Count != 0)
            {
                ApplyMemberListChanges();
            }
            FireMembershipEvent(events);
            _initialListFetched.Set();
        }

        public void HandleMemberAttributeChange(string uuid, string key, int operationType, string value)
        {
            var memberMap = _clusterService.GetMembersRef();
            if (memberMap == null)
            {
                return;
            }
            foreach (var target in memberMap.Values)
            {
                if (target.GetUuid().Equals(uuid))
                {
                    var type = (MemberAttributeOperationType) operationType;
                    ((Member) target).UpdateAttribute(type, key, value);
                    var memberAttributeEvent = new MemberAttributeEvent(_client.GetCluster(), target, type, key,
                        value);
                    _clusterService.FireMemberAttributeEvent(memberAttributeEvent);
                    break;
                }
            }
        }

        public virtual void BeforeListenerRegister()
        {
        }

        public virtual void OnListenerRegister()
        {
        }

        internal virtual void ListenMembershipEvents(Address ownerConnectionAddress)
        {
            Logger.Finest("Starting to listen for membership events from " + ownerConnectionAddress);
            _initialListFetched = new ManualResetEventSlim();
            try
            {
                var clientMessage = ClientMembershipListenerCodec.EncodeRequest();
                DistributedEventHandler handler = m => ClientMembershipListenerCodec.AbstractEventHandler
                    .Handle(m, HandleMember, HandleMemberCollection, HandleMemberAttributeChange);
                var future = _client.GetInvocationService().InvokeListenerOnTarget(clientMessage,
                    ownerConnectionAddress, handler, m => ClientMembershipListenerCodec.DecodeResponse(m).response);
                var response = ThreadUtil.GetResult(future);

                //registraiton id is ignored as this listener will never be removed
                var registirationId = ClientMembershipListenerCodec.DecodeResponse(response).response;

                WaitInitialMemberListFetched();
            }
            catch (Exception e)
            {
                if (_client.GetLifecycleService().IsRunning())
                {
                    if (Logger.IsFinestEnabled())
                    {
                        Logger.Warning("Error while registering to cluster events! -> " + ownerConnectionAddress, e);
                    }
                    else
                    {
                        Logger.Warning("Error while registering to cluster events! -> " + ownerConnectionAddress +
                                       ", Error: " + e);
                    }
                }
            }
        }

        /// <exception cref="System.Exception" />
        private void WaitInitialMemberListFetched()
        {
            var timeout = TimeUnit.SECONDS.ToMillis(InitialMembersTimeoutSeconds);
            var success = _initialListFetched.Wait((int) timeout);
            if (!success)
            {
                Logger.Warning("Error while getting initial member list from cluster!");
            }
        }

        private void MemberRemoved(IMember member)
        {
            _members.Remove(member);
            var connection = _connectionManager.GetConnection(member.GetAddress());
            if (connection != null)
            {
                _connectionManager.DestroyConnection(connection);
            }
            ApplyMemberListChanges();

            var @event = new MembershipEvent(_client.GetCluster(), member, MembershipEvent.MemberRemoved, GetMembers());
            _clusterService.FireMembershipEvent(@event);
        }

        private void ApplyMemberListChanges()
        {
            UpdateMembersRef();
            Logger.Info(_clusterService.MembersString());
        }

        private void FireMembershipEvent(IList<MembershipEvent> events)
        {
            foreach (var @event in events)
            {
                _clusterService.FireMembershipEvent(@event);
            }
        }

        private IList<MembershipEvent> DetectMembershipEvents(IDictionary<string, IMember> prevMembers)
        {
            IList<MembershipEvent> events = new List<MembershipEvent>();
            var eventMembers = GetMembers();
            foreach (var member in _members)
            {
                IMember former;
                prevMembers.TryGetValue(member.GetUuid(), out former);
                if (former == null)
                {
                    events.Add(new MembershipEvent(_client.GetCluster(), member, MembershipEvent.MemberAdded,
                        eventMembers));
                }
                else
                {
                    prevMembers.Remove(member.GetUuid());
                }
            }
            foreach (var member in prevMembers.Values)
            {
                events.Add(new MembershipEvent(_client.GetCluster(), member, MembershipEvent.MemberRemoved, eventMembers));
                var address = member.GetAddress();
                if (_clusterService.GetMember(address) == null)
                {
                    var connection = _connectionManager.GetConnection(address);
                    if (connection != null)
                    {
                        _connectionManager.DestroyConnection(connection);
                    }
                }
            }
            return events;
        }

        private void MemberAdded(IMember member)
        {
            _members.Add(member);
            ApplyMemberListChanges();
            var @event = new MembershipEvent(_client.GetCluster(), member, MembershipEvent.MemberAdded, GetMembers());
            _clusterService.FireMembershipEvent(@event);
        }

        private void UpdateMembersRef()
        {
            IDictionary<Address, IMember> map = new Dictionary<Address, IMember>(_members.Count);
            foreach (var member in _members)
            {
                map[member.GetAddress()] = member;
            }
            _clusterService.SetMembersRef(map);
        }

        private ICollection<IMember> GetMembers()
        {
            var set = new HashSet<IMember>();
            foreach (var member in _members)
            {
                set.Add(member);
            }
            return set;
        }
    }
}