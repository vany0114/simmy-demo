# Chaos injection using Simmy in a Microservice architecture
A microservice based application to demonstrate how chaos engineering works with [Simmy](https://github.com/Polly-Contrib/Simmy) using chaos policies in a distributed system.

## Prerequisites and Installation Requirements
1. Install [Docker for Windows](https://docs.docker.com/docker-for-windows/install/).
2. Install [.NET Core SDK](https://www.microsoft.com/net/download/windows)
3. Install [Visual Studio 2017](https://www.visualstudio.com/downloads/) 15.7 or later.
4. Share drives in Docker settings, in order to deploy and debug with Visual Studio 2017 (See the below image)
5. Clone this Repo
6. Set `docker-compose` project as startup project.
7. Press F5 and that's it!

![](https://github.com/vany0114/vany0114.github.io/blob/master/images/docker_settings_shared_drives.png)

> Note: The first time you hit F5 it'll take a few minutes, because in addition to compile the solution, it needs to pull/download the base images (SQL for Linux Docker, ASPNET, MongoDb and RabbitMQ images) and register them in the local image repo of your PC. The next time you hit F5 it'll be much faster.

### Tuning Docker for better performance
It is important to set Docker up properly with enough memory RAM and CPU assigned to it in order to improve the performance, or you will get errors when starting the containers with VS 2017 or "docker-compose up". Once Docker for Windows is installed in your machine, enter into its Settings and the Advanced menu option so you are able to adjust it to the minimum amount of memory and CPU (Memory: Around 4096MB and CPU:3) as shown in the image.

![](https://github.com/vany0114/vany0114.github.io/blob/master/images/docker_settings.png)

## The example
This repo provides an example/approach of how to use Simmy in a kind of real but simple scenario over a distributed architecture to inject chaos in your system in a configurable and automatic way.

The example demonstrates the following patterns with Simmy:

* Configuring StartUp so that Simmy chaos policies are only introduced in builds for certain environments.
* Configuring Simmy chaos policies to be injected into the app without changing any code, using a UI/API to update/get the chaos configuration.
* Injecting faults or chaos automatically by using a *WatchMonkey* (Azure function) specifying a frequency and duration of the chaos.

## The Architecture
![](https://github.com/vany0114/simmy-demo/blob/master/Architecture.png)

## The Chaos API
The example provides an API to save and get the chaos configuration in Redis Cache.

![](https://github.com/vany0114/chaos-injection-using-simmy/blob/master/demo-images/chaos-api.png)

## The Chaos UI - Configuring the chaos policies (monkeys)

The example provides a UI to set up the general chaos settings and also settings by operation level.

### General chaos settings
![](https://github.com/vany0114/chaos-injection-using-simmy/blob/master/demo-images/general-chaos-settings.png)

* **Enable Automatic Chaos Injection:**
Allows you to inject the chaos automatically based on a frequency and maximum chaos time duration.

* **Frequency:**
A `Timespan` indicating how often the chaos should be injected.

* **Max Duration:**
A `Timespan` indicating how long the chaos should take once is injected.

* **Enable Cluster Chaos:**
Allows you to inject chaos at cluster level. (This example uses Azure Service Fabric as orchestrator)

* **Percentage Nodes to Restart:**
An `int` between 0 and 100, indicating the percentage of nodes that should be restarted if cluster chaos is enabled.

* **Percentage Nodes To Stop:**
An `int` between 0 and 100, indicating the percentage of nodes that should be stopped if cluster chaos is enabled.

*  **Resource Group Name:**
The name of the resource group where the VM Scale Set of the cluster belongs to.

* **VM Scale Set Name:**
The name of the Virtual Machine Scale Set used by the cluster.

* **Injection Rsate:**
A `double` between 0 and 1, indicating what proportion of calls should be subject to failure-injection. For example, if 0.2, twenty percent of calls will be randomly affected; if 0.01, one percent of calls; if 1, all calls.

### Operations chaos settings
![](https://github.com/vany0114/chaos-injection-using-simmy/blob/master/demo-images/operation-chaos-settings-exception.png)

* **Operation:**
Which operation within your app these chaos settings apply to. Each call site in your codebase which uses Polly and Simmy can be tagged with an OperationKey. This is simply a string tag you choose, to identify different call paths in your app.

* **Duration:**
A `Timespan` indicating how long the chaos for a specific operation should take once is injected if Automatic Chaos Injection is enabled. (Optional)

* **Injection Rsate:**
A `double` between 0 and 1, indicating what proportion of calls should be subject to failure-injection. For example, if 0.2, twenty percent of calls will be randomly affected; if 0.01, one percent of calls; if 1, all calls.

* **Latency:**
If set, this much extra latency in ms will be added to affected calls, before the http request is made.

* **Exception:**
If set, affected calls will throw the given exception. (The original outbound http/sql/whatever call will not be placed.)

* **Status Code:**
If set, a result with the given http status code will be returned for affected calls. (The original outbound http call will not be placed.)

* **Enabled:**
A master switch for this call site. When true, faults may be injected at this call site per the other parameters; when false, no faults will be injected.

## The WatchMonkey
Is an Azure Function with a timer trigger which is executed every 5 minutes (value set arbitrarily for this example) in order to watch the monkeys (chaos settings/policies) set up in the previous UI. So, if the automatic chaos injection is enabled it releases all the monkeys for the given frequency within the time window configured (Max Duration), after that time window all the monkeys are caged (disabled) again. It also watches monkeys with a specific duration, allowing you to disable specific faults in a smaller window time.

## How the chaos is injected:
>Calls guarded by Polly policies often wrap a series of policies around a call using PolicyWrap. The policies in the PolicyWrap act as nesting middleware around the outbound call.
The recommended technique for introducing Simmy is to use one or more Simmy chaos policies as the innermost policies in a PolicyWrap.
By placing the chaos policies innermost, they subvert the usual outbound call at the last minute, substituting their fault or adding extra latency.
The existing Polly policies - further out in the PolicyWrap - still apply, so you can test how the Polly resilience you have configured handles the chaos/faults injected by Simmy.

## Adding Simmy chaos without changing existing configuration code
>As mentioned above, the usual technique to add chaos-injection is to configure Simmy policies innermost in your app's PolicyWraps.
One of the simplest ways to do this all across your app is to make all policies used in your app be stored in and drawn from PolicyRegistry. This is the technique demonstrated in this sample app.
In StartUp, all the Polly policies which will be used are configured, and registered in PolicyRegistry:

### Setting up some Http policies
```
var policyRegistry = services.AddPolicyRegistry();
policyRegistry["ResiliencePolicy"] = GetResiliencePolicy();

services.AddHttpClient<ResilientHttpClient>()
    .AddPolicyHandlerFromRegistry("ResiliencePolicy");
```

### Setting up some Azure SQL policies
```
services.AddSingleton<IPolicyAsyncExecutor>(sp =>
{
    var sqlPolicyBuilder = new SqlPolicyBuilder();
    return sqlPolicyBuilder
        .UseAsyncExecutor()
        .WithDefaultPolicies()
        .Build();
});
```

### Injecting chaos policies (monkeys) to our resilient strategies
>The `AddChaosInjectors()` extension method on IPolicyRegistry<> simply takes every policy in your PolicyRegistry and wraps Simmy policies (as the innermost policy) inside.

```
if (env.IsDevelopment() == false)
{
    // injects chaos to our Http policies defined previously.
    var httpPolicyRegistry = app.ApplicationServices.GetRequiredService<IPolicyRegistry<string>>();
    httpPolicyRegistry?.AddHttpChaosInjectors();
    
    // injects chaos to our Sql policies defined previously.
    var sqlPolicyExecutor = app.ApplicationServices.GetRequiredService<IPolicyAsyncExecutor>();
    sqlPolicyExecutor?.PolicyRegistry?.AddChaosInjectors();
}
```

>This allows you to inject Simmy into your app without changing any of your existing app configuration of Polly policies.
This extension method configures the policies in your PolicyRegistry with Simmy policies which react to chaos configured trhough the UI.

## References:
https://github.com/Polly-Contrib/Polly.Contrib.SimmyDemo_WebApi

***NOTE:*** for more details 
articles microservices
article resilient strategy
article explaining this example deeper is comming 
