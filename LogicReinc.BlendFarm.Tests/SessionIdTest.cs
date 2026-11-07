using LogicReinc.BlendFarm.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>
    /// Session/Task IDs are used as file names on render nodes and must not allow path traversal.
    /// </summary>
    [TestClass]
    public class SessionIdTest
    {
        [TestMethod]
        public void AcceptsGuids()
        {
            Assert.IsTrue(SessionUtil.IsSafeId(Guid.NewGuid().ToString()));
            Assert.IsTrue(SessionUtil.IsSafeId("abc_123-DEF"));
        }

        [TestMethod]
        public void RejectsPaths()
        {
            Assert.IsFalse(SessionUtil.IsSafeId(null));
            Assert.IsFalse(SessionUtil.IsSafeId(""));
            Assert.IsFalse(SessionUtil.IsSafeId(".."));
            Assert.IsFalse(SessionUtil.IsSafeId("..\\..\\Projects\\hero"));
            Assert.IsFalse(SessionUtil.IsSafeId("../etc/passwd"));
            Assert.IsFalse(SessionUtil.IsSafeId("C:\\Windows"));
            Assert.IsFalse(SessionUtil.IsSafeId("name.blend"));
            Assert.IsFalse(SessionUtil.IsSafeId(new string('a', 129)));
        }
    }
}
