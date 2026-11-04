// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Vault;

namespace Custode.Server.Tests.Vault;

public sealed class FolderPathTests
{
    [Theory]
    [InlineData("/production//aws/", "/production/aws")]
    [InlineData("/production/aws", "/production/aws")]
    [InlineData("//", "/")]
    [InlineData("/", "/")]
    public void Normalize_ProducesCanonicalAbsolutePath(string input, string expected)
    {
        Assert.Equal(expected, FolderPath.Normalize(input));
    }

    [Theory]
    [InlineData("production/aws")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("/production/../secrets")]
    [InlineData("/production/./aws")]
    public void Normalize_RejectsRelativeOrAmbiguousPaths(string? input)
    {
        Assert.Throws<InvalidVaultRequestException>(() => FolderPath.Normalize(input));
    }

    [Fact]
    public void ParentOf_ReturnsEnclosingFolder()
    {
        Assert.Equal("/production", FolderPath.ParentOf("/production/aws"));
        Assert.Equal("/", FolderPath.ParentOf("/production"));
        Assert.Null(FolderPath.ParentOf("/"));
    }

    [Fact]
    public void AncestorsAndSelf_ListsEveryPathFromRootToTheFolder()
    {
        Assert.Equal(["/", "/production", "/production/aws"], FolderPath.AncestorsAndSelf("/production/aws"));
    }
}
