﻿using Goldmint.Common;
using Goldmint.CoreLogic.Services.RuntimeConfig.Impl;
using Goldmint.WebApplication.Core.Response;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Goldmint.CoreLogic.Services.RuntimeConfig;
using System.IO;
using System.Text;
using Serilog;
using System.Collections.Generic;

namespace Goldmint.WebApplication {

	public partial class Startup {

		private readonly IHostingEnvironment _environment;
		private readonly IConfiguration _configuration;
		private readonly AppConfig _appConfig;
		private readonly RuntimeConfigHolder _runtimeConfigHolder;

		public Startup(IHostingEnvironment env, IConfiguration configuration) {
			_environment = env;
			_configuration = configuration;

			// globlization
			System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
			System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

			var curDir = Directory.GetCurrentDirectory();
			Console.OutputEncoding = Encoding.UTF8;

			// config
			try {
				var cfgDir = Environment.GetEnvironmentVariable("ASPNETCORE_CONFIGPATH");
		
				_configuration = new ConfigurationBuilder()
					.SetBasePath(cfgDir)
					.AddJsonFile("appsettings.json", optional: false)
					.AddJsonFile($"appsettings.{_environment.EnvironmentName}.json", optional: false)
					.Build()
				;
				
				_appConfig = new AppConfig();
				_configuration.Bind(_appConfig);
				
			} catch (Exception e) {
				throw new Exception("Failed to get app settings", e);
			}

			// serilog config
			var logConf = new LoggerConfiguration();
			{
				if (_environment.IsDevelopment()) {
					logConf.MinimumLevel.Verbose();
				}
				logConf.WriteTo.Console(outputTemplate: "{Timestamp:dd-MMM-yyyy-HH:mm} [{Level:u3}] {Message}   at {SourceContext}{NewLine}{Exception}");
			}
			var logger = Log.Logger = logConf.CreateLogger();
			
			logger.Information("Starting");

			// runtime config
			_runtimeConfigHolder = new RuntimeConfigHolder(logger);
		}

		public void Configure(IApplicationBuilder app, IApplicationLifetime applicationLifetime, IRuntimeConfigLoader runtimeConfigLoader) {

			applicationLifetime.ApplicationStopping.Register(OnServerStopRequested);
			applicationLifetime.ApplicationStopped.Register(OnServerStopped);

			// globlization
			app.UseRequestLocalization(new RequestLocalizationOptions {
				DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(System.Globalization.CultureInfo.InvariantCulture),
				SupportedCultures = new List<System.Globalization.CultureInfo> { System.Globalization.CultureInfo.InvariantCulture },
				SupportedUICultures = new List<System.Globalization.CultureInfo> { System.Globalization.CultureInfo.InvariantCulture },
			});

			// config loader
			_runtimeConfigHolder.SetLoader(runtimeConfigLoader);

			// setup ms logger
			// app.ApplicationServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().AddNLog();

			// nginx proxy
			{
				var forwardingOptions = new ForwardedHeadersOptions() {
					ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
					RequireHeaderSymmetry = false,
					ForwardLimit = null,
				};
				forwardingOptions.KnownNetworks.Clear();
				forwardingOptions.KnownProxies.Clear();
				app.UseForwardedHeaders(forwardingOptions);
			}

			// 503: response on exception
			app.UseExceptionHandler(builder => {
				builder.Run(async context => {
					var error = context.Features.Get<IExceptionHandlerFeature>();
					context.RequestServices?.GetService<ILogger>()?.ForContext(this.GetType())?.Error(error?.Error ?? new Exception("No extra data"), "Service failure");
					var resp = APIResponse.GeneralInternalFailure(error?.Error, !_environment.IsProduction());
					await resp.WriteResponse(context).ConfigureAwait(false);
				});
			});
			
			// 403: always write body if unathorized
			app.Use(async (context, next) => {
				await next();
				if (context.Response.StatusCode == 403) {
					var resp = APIResponse.BadRequest(APIErrorCode.Unauthorized);
					await resp.WriteResponse(context).ConfigureAwait(false);
				}
			});
			
			// check content type
			app.Use(async (context, next) => {
				var flatPath = context.Request.Path.ToString();

				if (context.Request.Method == "POST" &&  flatPath.StartsWith("/api/") && !flatPath.Contains("/callback/")) {
					if (!(context.Request.ContentType?.StartsWith("application/json") ?? false)) {
						var resp = APIResponse.BadRequest(APIErrorCode.InvalidContentType, "Json format is only allowed");
						await resp.WriteResponse(context).ConfigureAwait(false);
						return;
					}
				}
				await next();
			});

			// swagger
			if (!_environment.IsProduction()) {
				app.UseSwagger(opts => {
				});
				app.UseSwaggerUI(opts => {
					opts.SwaggerEndpoint("/" + ((_appConfig.Apps.RelativeApiPath).Trim('/') + "/swagger/api/swagger.json").Trim('/'), "API");
				});
			}

			// 404: redirect to index: not found, not a file, not api request
			app.Use(async (context, next) => {
				await next();
				if (context.Response.StatusCode == 404) {
					var resp = APIResponse.BadRequest(APIErrorCode.MethodNotFound);
					await resp.WriteResponse(context).ConfigureAwait(false);
				}
			});

			app.UseAuthentication();

			app.UseCors(opts => {
					opts.WithMethods("GET", "POST", "OPTIONS");
					opts.AllowAnyHeader();
					opts.AllowAnyOrigin();
				}
			);

			app.UseMvc();

			RunServices();
		}

		public void OnServerStopRequested() {
			Log.Logger.Information("Webserver stop requested");
		}

		public void OnServerStopped() {
			Log.Logger.Information("Webserver stopped");
			StopServices();
			Log.CloseAndFlush();
		}
	}
}
