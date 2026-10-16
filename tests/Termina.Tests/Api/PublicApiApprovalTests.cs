// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using PublicApiGenerator;

namespace Termina.Tests.Api;

public sealed class PublicApiApprovalTests
{
    [Fact]
    public Task Public_api_matches_the_approved_contract()
    {
        var publicApi = typeof(TerminaApplication).Assembly.GeneratePublicApi();

        return Verifier.Verify(publicApi)
            .UseDirectory("Snapshots");
    }
}
