using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LoxoneHueBridge.Web.Pages
{
    public class LogsModel : PageModel
    {
        private readonly ILogger<LogsModel> _logger;

        public LogsModel(ILogger<LogsModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            // Page setup - all functionality is handled via SignalR and JavaScript
            _logger.LogInformation("Logs page accessed");
        }
    }
}
