﻿using System.Threading;
using Hazelcast.Core;
using NUnit.Framework;

namespace Hazelcast.Client.Test
{
    [SetUpFixture]
    public class TestSetup
    {
       
        [TearDown]
        public void TearDown()
        {
            if (HazelcastBaseTest.Cluster != null)
            {
                HazelcastBaseTest.Cluster.Shutdown();       
            }
        }

    }
}