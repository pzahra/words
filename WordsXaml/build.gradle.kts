import org.jetbrains.intellij.platform.gradle.tasks.PrepareSandboxTask

// Minimal, backend-only Rider plugin build. Unlike the full JetBrains template this has no rdgen
// protocol and no Kotlin frontend — all logic lives in the .NET/ReSharper backend under src/dotnet.
// The IntelliJ Platform Gradle Plugin gives us the `runIde` sandbox for running/debugging in Rider.

plugins {
    id("java")
    id("org.jetbrains.intellij.platform") version "2.18.0"
}

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
        jetbrainsRuntime()
    }
}

val pluginVersion: String by project
val buildConfiguration: String by project
val dotNetPluginId: String by project
val riderSdkVersion: String by project

version = pluginVersion

val dotNetSrcDir = File(projectDir, "src/dotnet")

dependencies {
    intellijPlatform {
        // Sandbox Rider used by runIde; useInstaller=false is required for Rider distributions.
        rider(riderSdkVersion) {
            useInstaller = false
        }
        jetbrainsRuntime()
    }
}

intellijPlatform {
    pluginConfiguration {
        ideaVersion {
            sinceBuild = "253"
            // no upper bound: teammates update Rider faster than we cut plugin builds, and a
            // pinned until-build refuses to install on anything newer. The backend still
            // compiles against one ReSharper SDK — if a Rider update breaks its API surface,
            // the fix is bumping the SDK + riderSdkVersion, not re-pinning here.
            untilBuild = provider { null }
        }
    }
}

tasks {
    // Build the ReSharper backend. dotnet resolves JetBrains.ReSharper.SDK from nuget.org (already used
    // by the standalone build), so no local SDK/nuget wiring is needed.
    val compileDotNet by registering(Exec::class) {
        executable("dotnet")
        args(
            "build",
            "$dotNetSrcDir/WordsXaml/WordsXaml.csproj",
            "-consoleLoggerParameters:ErrorsOnly",
            "--configuration", buildConfiguration
        )
    }

    buildPlugin {
        dependsOn(compileDotNet)
    }

    // Copy the compiled backend into the plugin's dotnet/ folder — this is where the Rider ReSharper
    // host discovers and loads backend assemblies. The .pdb rides along so breakpoints bind.
    withType<PrepareSandboxTask> {
        dependsOn(compileDotNet)

        val outputFolder = file("$dotNetSrcDir/WordsXaml/bin/$buildConfiguration/net8.0")
        val pluginFiles = listOf(
            file("$outputFolder/$dotNetPluginId.dll"),
            file("$outputFolder/$dotNetPluginId.pdb"),
            file("$outputFolder/WordsXaml.Core.dll"),
            file("$outputFolder/WordsXaml.Core.pdb")
        )

        from(pluginFiles) {
            into("${rootProject.name}/dotnet")
        }

        doLast {
            if (!pluginFiles[0].exists())
                throw GradleException("Backend not built: ${pluginFiles[0]} is missing — did compileDotNet run?")
        }
    }

    runIde {
        jvmArgs("-Xmx1500m")
    }
}
