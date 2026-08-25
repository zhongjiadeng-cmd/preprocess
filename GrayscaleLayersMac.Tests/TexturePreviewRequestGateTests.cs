using System.Collections.Generic;
using GrayscaleLayersMac;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GrayscaleLayersMac.Tests;

[TestClass]
public sealed class TexturePreviewRequestGateTests
{
    [TestMethod]
    public void RunIfCurrent_SuppressesPreviousRequestMutation()
    {
        var gate = new TexturePreviewRequestGate();
        var previous = gate.BeginRequest();
        var current = gate.BeginRequest();
        var mutations = new List<string>();

        Assert.IsFalse(gate.RunIfCurrent(previous, () => mutations.Add("previous")));
        Assert.IsTrue(gate.RunIfCurrent(current, () => mutations.Add("current")));
        CollectionAssert.AreEqual(new[] { "current" }, mutations);
    }

    [TestMethod]
    public void Close_SuppressesCurrentAndFutureRequestMutations()
    {
        var gate = new TexturePreviewRequestGate();
        var active = gate.BeginRequest();
        var mutations = new List<string>();

        gate.Close();
        var afterClose = gate.BeginRequest();

        Assert.IsFalse(gate.RunIfCurrent(active, () => mutations.Add("active")));
        Assert.IsFalse(gate.RunIfCurrent(afterClose, () => mutations.Add("after-close")));
        Assert.HasCount(0, mutations);
    }
}
