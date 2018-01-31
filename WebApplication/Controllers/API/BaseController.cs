﻿using Goldmint.Common;
using Goldmint.CoreLogic.Services.Acquiring;
using Goldmint.CoreLogic.Services.Blockchain;
using Goldmint.CoreLogic.Services.KYC;
using Goldmint.CoreLogic.Services.Localization;
using Goldmint.CoreLogic.Services.Mutex;
using Goldmint.CoreLogic.Services.Notification;
using Goldmint.CoreLogic.Services.Rate;
using Goldmint.CoreLogic.Services.Ticket;
using Goldmint.DAL;
using Goldmint.DAL.Models.Identity;
using Goldmint.WebApplication.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using System;
using System.Threading.Tasks;

namespace Goldmint.WebApplication.Controllers.API {

	public abstract class BaseController : Controller {

		protected AppConfig AppConfig { get; private set; }
		protected IHostingEnvironment HostingEnvironment { get; private set; }
		protected ILogger Logger { get; private set; }
		protected ApplicationDbContext DbContext { get; private set; }
		protected IMutexHolder MutexHolder { get; private set; }
		protected SignInManager<User> SignInManager { get; private set; }
		protected UserManager<User> UserManager { get; private set; }
		protected IKycProvider KycExternalProvider { get; private set; }
		protected INotificationQueue EmailQueue { get; private set; }
		protected ITemplateProvider TemplateProvider { get; private set; }
		protected ICardAcquirer CardAcquirer { get; private set; }
		protected ITicketDesk TicketDesk { get; private set; }
		protected IEthereumReader EthereumObserver { get; private set; }
		protected IGoldRateProvider GoldRateProvider {get; private set;}

		protected BaseController() { }

		[NonAction]
		private void InitServices(IServiceProvider services) {
			Logger = services.GetLoggerFor(this.GetType());
			AppConfig = services.GetRequiredService<AppConfig>();
			HostingEnvironment = services.GetRequiredService<IHostingEnvironment>();
			DbContext = services.GetRequiredService<ApplicationDbContext>();
			MutexHolder = services.GetRequiredService<IMutexHolder>();
			SignInManager = services.GetRequiredService<SignInManager<User>>();
			UserManager = services.GetRequiredService<UserManager<User>>();
			KycExternalProvider = services.GetRequiredService<IKycProvider>();
			EmailQueue = services.GetRequiredService<INotificationQueue>();
			TemplateProvider = services.GetRequiredService<ITemplateProvider>();
			CardAcquirer = services.GetRequiredService<ICardAcquirer>();
			TicketDesk = services.GetRequiredService<ITicketDesk>();
			EthereumObserver = services.GetRequiredService<IEthereumReader>();
			GoldRateProvider = services.GetRequiredService<IGoldRateProvider>();
		}

		// ---

		[NonAction]
		public override void OnActionExecuted(ActionExecutedContext context) {
			InitServices(context?.HttpContext?.RequestServices);
			base.OnActionExecuted(context);
		}

		[NonAction]
		public override void OnActionExecuting(ActionExecutingContext context) {
			base.OnActionExecuting(context);
		}

		[NonAction]
		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) {
			InitServices(context?.HttpContext?.RequestServices);
			await base.OnActionExecutionAsync(context, next);
		}

		// ---

		[NonAction]
		public string MakeLink(string path = null, string query = null, string fragment = null) {
			var uri = new UriBuilder(
				HttpContext.Request.Scheme,
				HttpContext.Request.Host.Host,
				HttpContext.Request.Host.Port ?? 443
			);
			if (path != null) {
				uri.Path = path;
			}
			if (query != null) {
				uri.Query = query;
			}
			if (fragment != null) {
				uri.Fragment = fragment;
			}
			return uri.ToString();
		}

		[NonAction]
		protected bool IsUserAuthenticated() {
			return HttpContext.User?.Identity.IsAuthenticated ?? false;
		}

		[NonAction]
		protected async Task<User> GetUserFromDb() {
			if (IsUserAuthenticated()) {
				var name = UserManager.NormalizeKey(HttpContext.User.Identity.Name);
				return await DbContext.Users
					.Include(user => user.UserOptions)
					.Include(user => user.UserVerification)
					.Include(user => user.Card)
					.FirstAsync(user => user.NormalizedUserName == name)
				;
			}
			return null;
		}

		[NonAction]
		protected UserAgentInfo GetUserAgentInfo() {

			var ip = HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString();
			
			// ip object
			var ipObj = System.Net.IPAddress.Parse(ip);

			// agent
			var agent = "Unknown";
			if (HttpContext.Request.Headers.TryGetValue("User-Agent", out var agentParsed)) {
				agent = agentParsed.ToString();
			}

			return new UserAgentInfo() {
				Ip = ip,
				IpObject = ipObj,
				Agent = agent,
			};
		}
	}
}
