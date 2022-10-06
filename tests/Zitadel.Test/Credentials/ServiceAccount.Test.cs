﻿using FluentAssertions;
using Xunit;
using Zitadel.Credentials;

namespace Zitadel.Test.Credentials;

public class ServiceAccountTest
{
    [Fact]
    public async Task Load_App_From_Json()
    {
        var sa = await ServiceAccount.LoadFromJsonStringAsync(TestData.ServiceAccountJson);
        sa.UserId.Should().Be("170079991923474689");
    }
    
    [Fact]
    public async Task Authenticate_Correctly()
    {
        var sa = await ServiceAccount.LoadFromJsonStringAsync(TestData.ServiceAccountJson);
        var token = await sa.AuthenticateAsync(TestData.ApiUrl);

        token.Should().NotBeEmpty();
    }
}
