using System.ComponentModel;
using PatTech.Utils;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>A long enum, past what an int holds.</summary>
public enum WideEnum : long {
	Far = 1L << 40,
}

/// <summary>A byte enum.</summary>
public enum TinyEnum : byte {
	Top = 255,
}

/// <summary>A member with a Description and one without; none has a [Words] key, so nothing looks Words.Known up.</summary>
[Flags]
public enum DescribedFlags {
	None = 0,
	[Description("Two sugars")] Sugar = 1,
	Cream = 2,
}

/// <summary>
/// Covers <see cref="Extensions.Describe(Enum, string?, IWords?)"/> where it needs no words.
/// </summary>
public class DescribeTests {

	[Fact]
	public void Number_IsTheValue_WhateverTheUnderlyingType() {
		Assert.Equal("1099511627776", WideEnum.Far.Describe("D"));
		Assert.Equal("255", TinyEnum.Top.Describe("D"));
		Assert.Equal("3", (DescribedFlags.Sugar | DescribedFlags.Cream).Describe("D"));
	}

	[Fact]
	public void NothingForTheFormat_IsEmpty() {
		Assert.Equal("Two sugars", DescribedFlags.Sugar.Describe("d"));
		Assert.Equal("", DescribedFlags.Cream.Describe("d"));
		Assert.Equal("", DescribedFlags.Cream.Describe("T"));
	}
}
