﻿namespace Craftsman.Tests
{
    using Craftsman.Helpers;
    using FluentAssertions;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Xunit;

    public class ClassPathHelperTests
    {
        [Fact]
        public void ControllerClassPath_returns_accurate_path()
        {
            var path = ClassPathHelper.ControllerClassPath("", "ProductName.cs");

            path.ClassDirectory.Should().Be(Path.Combine("WebApi", "Controllers", "v1"));
        }
    }
}
