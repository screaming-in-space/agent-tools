using Agent.SDK.Console;

namespace Agent.SDK.Tests;

public class RingBufferTests
{
    [Fact]
    public void Add_BelowCapacity_AllItemsPresent()
    {
        var buffer = new RingBuffer<int>(5);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        Assert.Equal(3, buffer.Count);
        Assert.Equal([1, 2, 3], buffer.ToList());
    }

    [Fact]
    public void Add_AtCapacity_OldestDropped()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);

        Assert.Equal(3, buffer.Count);
        Assert.Equal([2, 3, 4], buffer.ToList());
    }

    [Fact]
    public void Add_WrapAround_CorrectOrder()
    {
        var buffer = new RingBuffer<int>(3);
        for (var i = 1; i <= 7; i++)
        {
            buffer.Add(i);
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal([5, 6, 7], buffer.ToList());
    }

    [Fact]
    public void Empty_ReturnsEmptyList()
    {
        var buffer = new RingBuffer<string>(5);

        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.ToList());
    }

    [Fact]
    public void Newest_ReturnsLastAdded()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(10);
        buffer.Add(20);
        buffer.Add(30);

        Assert.Equal(30, buffer.Newest);
    }

    [Fact]
    public void Newest_Empty_ReturnsDefault()
    {
        var buffer = new RingBuffer<int>(3);

        Assert.Equal(0, buffer.Newest);
    }

    [Fact]
    public void Clear_ResetsBuffer()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.ToList());
    }

    [Fact]
    public void Capacity_MatchesConstructorArg()
    {
        var buffer = new RingBuffer<int>(42);

        Assert.Equal(42, buffer.Capacity);
    }

    [Fact]
    public void SingleCapacity_AlwaysHoldsOneItem()
    {
        var buffer = new RingBuffer<string>(1);
        buffer.Add("first");
        buffer.Add("second");

        Assert.Equal(1, buffer.Count);
        Assert.Equal(["second"], buffer.ToList());
    }
}
