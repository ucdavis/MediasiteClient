using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MediasiteUtil;
using MediasiteUtil.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sample.Web.Models;

namespace Sample.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _config;
        private readonly MediasiteClient _mediasite;

        public HomeController(ILogger<HomeController> logger, IConfiguration config, MediasiteClient mediasite)
        {
            _logger = logger;
            _config = config;
            _mediasite = mediasite;
            mediasite.Config = config.GetSection("MediasiteConfig").Get<MediasiteConfig>();
        }

        public IActionResult Index()
        {
            var folder = _mediasite.FindFolder("Mediasite");
            return View(folder);
        }

        public IActionResult Templates()
        {
            var templates = _mediasite.GetTemplates().OrderBy(t => t.Name);
            return View(templates);
        }

        public IActionResult Recorders()
        {
            var recorders = _mediasite.GetRecorders().OrderBy(r => r.Name);
            return View(recorders);
        }

        public IActionResult Schedules(string? recorderId)
        {
            IEnumerable<Schedule> schedules;
            if (recorderId is null)
            {
                var schedule = _mediasite.FindSchedule("Morri - Law 200A - U S Legal Seminar (LLM) | 90244");
                schedules = new List<Schedule>() { schedule };
            }
            else
            {
                schedules = _mediasite.GetSchedulesWithFilter($"RecorderId eq '{recorderId}'");
            }

            return View(schedules);
        }

        public IActionResult Recurrences(string scheduleId)
        {
            if (String.IsNullOrEmpty(scheduleId))
            {
                return RedirectToAction("Schedules");
            }

            var recurrences = _mediasite.GetScheduleRecurrences(scheduleId);
            return View(recurrences);
        }

        public IActionResult LawChannel(bool auth = false)
        {
            var channel = _mediasite.FindChannel("Law");
            if (auth)
            {
                _mediasite.GetAuthTicket(channel.Id, 120, "bob");
            }
            return View(channel);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
