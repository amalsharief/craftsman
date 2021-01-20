﻿namespace Craftsman.Commands
{
    using Craftsman.Builders;
    using Craftsman.Builders.Dtos;
    using Craftsman.Builders.Seeders;
    using Craftsman.Builders.Tests.Fakes;
    using Craftsman.Builders.Tests.IntegrationTests;
    using Craftsman.Builders.Tests.RepositoryTests;
    using Craftsman.Enums;
    using Craftsman.Exceptions;
    using Craftsman.Helpers;
    using Craftsman.Models;
    using Craftsman.Removers;
    using FluentAssertions.Common;
    using LibGit2Sharp;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Abstractions;
    using System.Linq;
    using YamlDotNet.Serialization;
    using static Helpers.ConsoleWriter;

    public static class NewMicroCommand
    {
        public static void Help()
        {
            WriteHelpHeader(@$"Description:");
            WriteHelpText(@$"   Scaffolds out one or more microservices in a new project based on a given template file in a json or yaml format.{Environment.NewLine}");

            WriteHelpHeader(@$"Usage:");
            WriteHelpText(@$"   craftsman new:micro [options] <filepath>");
            WriteHelpText(@$"   OR");
            WriteHelpText(@$"   craftsman new:microservice [options] <filepath>{Environment.NewLine}");

            WriteHelpHeader(@$"Arguments:");
            WriteHelpText(@$"   filepath         The full filepath for the yaml or json file that describes your microservice using a proper Wrapt format.");

            WriteHelpText(Environment.NewLine);
            WriteHelpHeader(@$"Options:");
            WriteHelpText(@$"   -h, --help          Display this help message. No filepath is needed to display the help message.");

            WriteHelpText(Environment.NewLine);
            WriteHelpHeader(@$"Example:");
            WriteHelpText(@$"       craftsman new:micro C:\fullpath\micro.yaml");
            WriteHelpText(@$"       craftsman new:micro C:\fullpath\micro.yml");
            WriteHelpText(@$"       craftsman new:micro C:\fullpath\micro.json{Environment.NewLine}");

        }

        public static void Run(string filePath, string buildSolutionDirectory, IFileSystem fileSystem)
        {
            try
            {
                GlobalSingleton instance = GlobalSingleton.GetInstance();

                FileParsingHelper.RunInitialTemplateParsingGuards(filePath);
                var template = FileParsingHelper.GetTemplateFromFile<MicroTemplate>(filePath);
                WriteHelpText($"Your template file was parsed successfully.");

                foreach(var micro in template.Microservices)
                {
                    FileParsingHelper.RunPrimaryKeyGuard(micro.Entities);
                }
                FileParsingHelper.RunSolutionNameAssignedGuard(template.SolutionName);

                // solution level stuff
                var solutionDirectory = $"{buildSolutionDirectory}{Path.DirectorySeparatorChar}{template.SolutionName}";
                ReadmeBuilder.CreateReadme(solutionDirectory, template.SolutionName, fileSystem);
                if (template.AddGit)
                    GitSetup(solutionDirectory);

                // add src folder
                fileSystem.Directory.CreateDirectory(Path.Combine(solutionDirectory, "src"));
                solutionDirectory = Path.Combine(solutionDirectory, "src");
                SolutionBuilder.BuildSolution(solutionDirectory, template.SolutionName, fileSystem);


                // add all files based on the given template config
                RunMicroTemplateBuilders(solutionDirectory, template.Microservices, template.Gateways, fileSystem);

                WriteFileCreatedUpdatedResponse();
                WriteHelpHeader($"{Environment.NewLine}Your API is ready! Build something amazing.");
                StarGithubRequest();
            }
            catch (Exception e)
            {
                if (e is FileAlreadyExistsException
                    || e is DirectoryAlreadyExistsException
                    || e is InvalidSolutionNameException
                    || e is FileNotFoundException
                    || e is InvalidDbProviderException
                    || e is InvalidFileTypeException)
                {
                    WriteError($"{e.Message}");
                }
                else
                    WriteError($"An unhandled exception occurred when running the API command.\nThe error details are: \n{e.Message}");
            }
        }

        private static void RunMicroTemplateBuilders(string solutionDirectory, List<Microservice> microservices, List<Gateway> gateways, IFileSystem fileSystem)
        {
            // services path
            var servicesPath = Path.Combine(solutionDirectory, "services");
            fileSystem.Directory.CreateDirectory(servicesPath);

            foreach (var micro in microservices)
            {
                // set micro path
                var microPath = Path.Combine(servicesPath, micro.ProjectFolderName);

                // add projects
                SolutionBuilder.AddMicroServicesProjects(solutionDirectory, microPath, micro.DbContext.Provider, micro.ProjectFolderName, fileSystem, micro.AuthSetup.InMemoryUsers);

                // dbcontext
                DbContextBuilder.CreateDbContext(microPath, micro.Entities, micro.DbContext.ContextName, micro.AuthSetup.AuthMethod, micro.DbContext.Provider, micro.DbContext.DatabaseName);

                //entities
                foreach (var entity in micro.Entities)
                {
                    EntityBuilder.CreateEntity(microPath, entity, fileSystem);
                    DtoBuilder.CreateDtos(microPath, entity);

                    RepositoryBuilder.AddRepository(microPath, entity, micro.DbContext);
                    ValidatorBuilder.CreateValidators(microPath, entity);
                    ProfileBuilder.CreateProfile(microPath, entity);

                    ControllerBuilder.CreateController(microPath, entity, micro.SwaggerConfig.AddSwaggerComments);

                    FakesBuilder.CreateFakes(microPath, micro.ProjectFolderName, entity);
                    ReadTestBuilder.CreateEntityReadTests(microPath, micro.ProjectFolderName, entity, micro.DbContext.ContextName, micro.AuthSetup.AuthMethod);
                    GetTestBuilder.CreateEntityGetTests(microPath, micro.ProjectFolderName, entity, micro.DbContext.ContextName);
                    PostTestBuilder.CreateEntityWriteTests(microPath, entity, micro.ProjectFolderName);
                    UpdateTestBuilder.CreateEntityUpdateTests(microPath, entity, micro.ProjectFolderName, micro.DbContext.ContextName);
                    DeleteTestBuilder.DeleteEntityWriteTests(microPath, entity, micro.ProjectFolderName, micro.DbContext.ContextName, micro.AuthSetup.AuthMethod);
                    WebAppFactoryBuilder.CreateWebAppFactory(microPath, micro.ProjectFolderName, micro.DbContext.ContextName);
                }


                // environments
                AddStartupEnvironmentsWithServices(
                    microPath,
                    micro.ProjectFolderName,
                    micro.AuthSetup.AuthMethod,
                    micro.DbContext.DatabaseName,
                    micro.Environments,
                    micro.SwaggerConfig,
                    micro.AuthSetup.InMemoryUsers,
                    micro.Port
                );

                //seeders
                SeederBuilder.AddSeeders(microPath, micro.Entities, micro.DbContext.ContextName);

                //services
                SwaggerBuilder.AddSwagger(microPath, micro.SwaggerConfig, micro.ProjectFolderName);

                if (micro.AuthSetup.AuthMethod == "JWT")
                {
                    IdentityServicesModifier.SetIdentityOptions(microPath, micro.AuthSetup);
                    IdentitySeederBuilder.AddSeeders(microPath, micro.AuthSetup.InMemoryUsers);
                    IdentityRoleBuilder.CreateRoles(microPath, micro.AuthSetup.Roles);
                    RoleSeedBuilder.SeedRoles(microPath, micro.AuthSetup.InMemoryUsers);
                }
            }

            // gateway path
            var gatewayPath = Path.Combine(solutionDirectory, "gateways");
            fileSystem.Directory.CreateDirectory(gatewayPath);

            foreach (var gateway in gateways)
            {
                SolutionBuilder.AddGatewayProject(solutionDirectory, gatewayPath, gateway.GatewayProjectName, fileSystem);
                
                foreach(var env in gateway.EnvironmentGateways)
                {
                    //TODO: run quality checks that profile name exists, gateway url is a valid path and https, Env Name

                    if (env.EnvironmentName != "Startup")
                        StartupBuilder.CreateGatewayStartup(gatewayPath, env.EnvironmentName, gateway.GatewayProjectName);

                    GatewayAppSettingsBuilder.CreateAppSettings(gatewayPath, env, gateway.GatewayProjectName, microservices);
                    GatewayLaunchSettingsModifier.AddProfile(gatewayPath, env, gateway.GatewayProjectName);
                }

            }
        }

        private static void GitSetup(string solutionDirectory)
        {
            GitBuilder.CreateGitIgnore(solutionDirectory);

            Repository.Init(solutionDirectory);
            var repo = new Repository(solutionDirectory);

            string[] allFiles = Directory.GetFiles(solutionDirectory, "*.*", SearchOption.AllDirectories);
            Commands.Stage(repo, allFiles);

            var author = new Signature("Craftsman", "craftsman", DateTimeOffset.Now);
            repo.Commit("Initial Commit", author, author);
        }

        private static void UpdateFoundation()
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = @$"new Foundation.Api --update-apply",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                }
            };

            process.Start();
            process.WaitForExit();
        }

        private static void UninstallFoundation()
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = @$"new -u Foundation.Api",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                }
            };

            process.Start();
            process.WaitForExit();
        }

        private static void InstallFoundation()
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = @$"new -i Foundation.Api",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                }
            };

            process.Start();
            process.WaitForExit();
        }

        private static void AddStartupEnvironmentsWithServices(
            string solutionDirectory,
            string solutionName,
            string authMethod,
            string databaseName,
            List<ApiEnvironment> environments,
            SwaggerConfig swaggerConfig,
            List<ApplicationUser> inMemoryUsers,
            int port)
        {
            // add a development environment by default for local work if none exists
            if (environments.Where(e => e.EnvironmentName == "Development").Count() == 0)
                environments.Add(new ApiEnvironment { EnvironmentName = "Development", ProfileName = $"{solutionName} (Development)" });

            foreach (var env in environments)
            {
                // default startup is already built in cleanup phase
                if(env.EnvironmentName != "Startup")
                    StartupBuilder.CreateWebApiStartup(solutionDirectory, env.EnvironmentName, authMethod, inMemoryUsers);

                WebApiAppSettingsBuilder.CreateAppSettings(solutionDirectory, env, databaseName, authMethod);
                WebApiLaunchSettingsModifier.AddProfile(solutionDirectory, env, port);

                //services
                if (!swaggerConfig.IsSameOrEqualTo(new SwaggerConfig()))
                    SwaggerBuilder.RegisterSwaggerInStartup(solutionDirectory, env);
            }
        }
    }
}