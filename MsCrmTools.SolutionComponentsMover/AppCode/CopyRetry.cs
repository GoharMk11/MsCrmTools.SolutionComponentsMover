using Microsoft.Xrm.Sdk;
using System;
using System.Net;
using System.Diagnostics;
using System.Globalization;
using System.ServiceModel;
using System.Threading;

namespace MsCrmTools.SolutionComponentsMover.AppCode
{
    internal sealed class RetryOutcome
    {
        public string Status, Error = "", Notes = "";
        public int Attempts;
    }

    internal static class CopyRetry
    {
        private static readonly int[] BusyCodes = { -2147015902, -2147015903, -2147015898 };

        public static bool IsTransient(Exception error)
        {
            if (error is FaultException<OrganizationServiceFault> fault)
            {
                for (var detail = fault.Detail; detail != null; detail = detail.InnerFault)
                    if (Array.IndexOf(BusyCodes, detail.ErrorCode) >= 0) return true;
                return false; // Never retry a business/security fault just because it is a CommunicationException.
            }
            if (error is FaultException) return false;
            if (error is TimeoutException || error is ServerTooBusyException || error is EndpointNotFoundException ||
                error is CommunicationObjectAbortedException || error is CommunicationObjectFaultedException) return true;
            if (error is WebException web)
            {
                if (web.Response is HttpWebResponse response)
                {
                    var status = (int)response.StatusCode;
                    return status == 429 || status == 502 || status == 503 || status == 504;
                }
                return web.Status == WebExceptionStatus.Timeout || web.Status == WebExceptionStatus.ConnectFailure || web.Status == WebExceptionStatus.ConnectionClosed ||
                    web.Status == WebExceptionStatus.ReceiveFailure || web.Status == WebExceptionStatus.SendFailure || web.Status == WebExceptionStatus.KeepAliveFailure;
            }
            return error.InnerException != null && IsTransient(error.InnerException);
        }

        private static TimeSpan Delay(Exception error, int attempt)
        {
            var delay = TimeSpan.FromSeconds(attempt); // 1s, 2s; maximum 3 total writes.
            if (error is FaultException<OrganizationServiceFault> fault)
            {
                for (var detail = fault.Detail; detail != null; detail = detail.InnerFault)
                {
                    object value;
                    if (detail.ErrorDetails.TryGetValue("Retry-After", out value))
                    {
                        if (value is TimeSpan span && span > delay) delay = span;
                        else if (value is int seconds && seconds > delay.TotalSeconds) delay = TimeSpan.FromSeconds(seconds);
                    }
                }
            }
            if (error is WebException web && web.Response is HttpWebResponse response)
            {
                var header = response.Headers["Retry-After"];
                double seconds;
                DateTimeOffset date;
                if (double.TryParse(header, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && !double.IsNaN(seconds) && seconds > delay.TotalSeconds)
                    delay = TimeSpan.FromSeconds(Math.Min(seconds, 61));
                else if (DateTimeOffset.TryParse(header, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date) && date > DateTimeOffset.UtcNow + delay)
                    delay = TimeSpan.FromSeconds(Math.Min((date - DateTimeOffset.UtcNow).TotalSeconds, 61));
            }
            if (error.InnerException != null)
            {
                var inner = Delay(error.InnerException, attempt);
                if (inner > delay) delay = inner;
            }
            return delay;
        }

        public static RetryOutcome Execute(Action write, Func<bool> verify, Func<bool> cancelled, Action<int, string> notify = null, Action<TimeSpan> testWait = null)
        {
            var result = new RetryOutcome();
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                if (cancelled()) { result.Status = result.Attempts == 0 ? "Not processed" : "Unconfirmed"; result.Error = "Cancelled before another request; any earlier in-flight request may have completed."; return result; }
                try
                {
                    result.Attempts = attempt;
                    write(); result.Status = "Added"; return result;
                }
                catch (StaleContentException stale) { stale.PreviousWriteAttempts = attempt - 1; throw; }
                catch (Exception error)
                {
                    if (!IsTransient(error)) { result.Status = "Failed"; result.Error = error.Message; return result; }
                    var delay = Delay(error, attempt);
                    result.Notes = "Transient error after attempt " + attempt + ": " + error.Message;
                    notify?.Invoke(attempt, "Temporary CRM error. Waiting before target verification; completed additions remain.");
                    if (!cancelled() && delay <= TimeSpan.FromSeconds(60))
                    {
                        if (testWait != null) testWait(delay);
                        else
                        {
                            var elapsed = Stopwatch.StartNew();
                            while (!cancelled() && elapsed.Elapsed < delay) Thread.Sleep(100);
                        }
                    }
                    try
                    {
                        if (verify()) { result.Status = "Added"; result.Notes += " Target coverage confirmed; no repeated write was needed."; return result; }
                    }
                    catch (Exception verificationError)
                    {
                        result.Status = "Unconfirmed"; result.Error = error.Message + " Outcome could not be confirmed: " + verificationError.Message + ". No blind retry was made."; return result;
                    }
                    if (cancelled()) { result.Status = error is FaultException<OrganizationServiceFault> ? "Failed" : "Unconfirmed"; result.Error = "Cancelled before retry; target verification did not confirm the required coverage. " + error.Message; return result; }
                    if (attempt == 3 || delay > TimeSpan.FromSeconds(60))
                    {
                        result.Status = error is FaultException<OrganizationServiceFault> ? "Failed" : "Unconfirmed"; result.Error = error.Message + (delay > TimeSpan.FromSeconds(60) ? " Server retry delay exceeds 60 seconds; retry later." : " Maximum of 3 write attempts reached."); return result;
                    }
                    notify?.Invoke(attempt, "Target does not yet satisfy coverage. Retrying the same component (attempt " + (attempt + 1) + " of 3).");
                }
            }
            return result;
        }
    }
}
