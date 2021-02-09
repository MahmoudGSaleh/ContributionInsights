using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSON_Parser_GitHub
{
    enum ContributionType
    {
        ExternalPull,
        ExternalIssue,
        ExternalComment,
        InternalPull,
        InternalIssue,
        InternalComment
    };
    class Program
    {
        private static Dictionary<ContributionType, int> stats = new Dictionary<ContributionType, int>()
        {
            { ContributionType.ExternalComment, 0 },
            { ContributionType.ExternalIssue, 0 },
            { ContributionType.ExternalPull, 0 },
            { ContributionType.InternalComment, 0 },
            { ContributionType.InternalIssue, 0 },
            { ContributionType.InternalPull, 0 }
        };


        private static HashSet<string> contributors = new HashSet<string>();

        // This is loaded from the file KnownContributers.txt
        private static string[] KnownInternalContributors = { };

        // In the future, these can be passed as arguments to the application
        const string RepoOwner = "microsoft";
        const string RepoName = "STL";
        const int MaxPerPage = 100;
        private async static Task<string> DownloadPageAsync(GitHubClient client, DateTime fromDate, DateTime toDate)
        {
            StringBuilder content = new StringBuilder("Login\tUrl\tState\tAssociation\tType\tRelationType\tCreatedAt\tUpdatedAt\n");
            await GetIssues(client, content, fromDate, toDate);

            HashSet<int> commentsAdded = new HashSet<int>();
            await GetComments(client, content, fromDate, toDate, false, commentsAdded);
            await GetComments(client, content, fromDate, toDate, true, commentsAdded);

            return await Task.FromResult(content.ToString());
        }

        private static async Task GetIssues(GitHubClient client, StringBuilder content, DateTime fromDate, DateTime toDate)
        {
            var searchRequest = new SearchIssuesRequest
            {
                Repos = new RepositoryCollection()
                {
                    $"{RepoOwner}/{RepoName}"
                },
                PerPage = MaxPerPage,
                SortField = IssueSearchSort.Created,
                Order = SortDirection.Ascending,
                Created = new DateRange(fromDate, toDate)
            };

            int totalFound = 0;
            bool hasMoreResults = true;
            List<Issue> currentIssues = new List<Issue>();
            while (hasMoreResults)
            {
                var issues = await client.Search.SearchIssues(searchRequest);
                foreach (var issue in issues.Items)
                {
                    contributors.Add(issue.User.Login);
                    bool isInternal = KnownInternalContributors.Contains(issue.User.Login);
                    bool isPull = issue.PullRequest != null;
                    string issueType = isPull ? "Pull" : "Issue";
                    string relation = StoreAndGetIssueRelation(isInternal, issueType);
                    content.AppendLine(issue.User.Login + "\t" + issue.HtmlUrl + "\t" + issue.State + "\t" + issue.User.Permissions + "\t" + issueType + "\t" + relation + "\t" + issue.CreatedAt + "\t" + issue.UpdatedAt);
                    currentIssues.Add(issue);
                }
                totalFound += issues.Items.Count;
                hasMoreResults = issues.TotalCount > totalFound;
                if (hasMoreResults)
                {
                    searchRequest.Page++;
                }
            }
        }

        private static string StoreAndGetIssueRelation(bool isInternal, string issueType)
        {
            string relation = (isInternal ? "Internal" : "External") + issueType;
            ContributionType contributionType = Enum.Parse<ContributionType>(relation);
            stats[contributionType]++;
            return relation;
        }

        private static async Task GetComments(GitHubClient client, StringBuilder content, DateTime fromDate, DateTime toDate, bool isSortByCreated, HashSet<int> commentsAdded)
        {
            IssueCommentsClient issueCommentsClient = new IssueCommentsClient(new ApiConnection(client.Connection));
            IssueCommentRequest commentRequest = new IssueCommentRequest()
            {
                Since = fromDate,
                Direction = SortDirection.Ascending,
                Sort = isSortByCreated ? IssueCommentSort.Created : IssueCommentSort.Updated
            };

            ApiOptions commentOptions = new ApiOptions()
            {
                PageSize = MaxPerPage,
                PageCount = 1,
                StartPage = 1
            };

            bool hasMoreComments = true;
            while (hasMoreComments)
            {
                var comments = await issueCommentsClient.GetAllForRepository(RepoOwner, RepoName, commentRequest, commentOptions);
                foreach (var comment in comments)
                {
                    bool isInDateRange = isSortByCreated ? (comment.CreatedAt >= fromDate && comment.CreatedAt <= toDate) : (comment.UpdatedAt >= fromDate && comment.UpdatedAt <= toDate);
                    if (isInDateRange)
                    {

                        if (!commentsAdded.Contains(comment.Id))
                        {
                            commentsAdded.Add(comment.Id);

                            bool isInternal = KnownInternalContributors.Contains(comment.User.Login);
                            string issueType = "Comment";
                            string relation = StoreAndGetIssueRelation(isInternal, issueType);

                            content.AppendLine(comment.User.Login + "\t" + comment.HtmlUrl + "\t" + "NA" + "\t" + comment.AuthorAssociation + "\t" + issueType + "\t" + relation + "\t" + comment.CreatedAt + "\t" + comment.UpdatedAt);
                            contributors.Add(comment.User.Login);
                        }
                    }
                    else
                    {
                        // Outside date range
                        hasMoreComments = false;
                        break; // from inner loop
                    }
                }
                if (hasMoreComments && comments.Count == MaxPerPage)
                {
                    commentOptions.StartPage++;
                }
                else
                {
                    hasMoreComments = false;
                }
            }
        }

        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Invalid arguments.");
                Usage();
                return;
            }

            DateTime fromDate;
            DateTime toDate;

            try
            {
                fromDate = DateTime.Parse(args[0]);
                toDate = DateTime.Parse(args[1]);
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to parse dates: " + e.Message);
                Usage();
                return;

            }

            if (toDate < fromDate)
            {
                Console.WriteLine("Invalid dates.");
                Usage();
                return;
            }

            try
            {
                KnownInternalContributors = File.ReadAllLines("KnownContributors.txt");
            }
            catch (Exception e)
            {
                Console.WriteLine("Warning! KnownContributors data will be ignored: " + e.Message);
            }
            try
            {
                var client = new GitHubClient(new ProductHeaderValue("MSaleh-App-2"));
                string output = DownloadPageAsync(client, fromDate, toDate).GetAwaiter().GetResult();
                string fileName = "issues_" + fromDate.Year + "-" + fromDate.Month + "-" + fromDate.Day + "_" + toDate.Year + "-" + toDate.Month + "-" + toDate.Day + ".txt";
                File.WriteAllText(fileName, output);

                StringBuilder summaryOutput = new StringBuilder();
                summaryOutput.AppendLine("From: " + fromDate);
                summaryOutput.AppendLine("To:   " + toDate);
                summaryOutput.AppendLine();

                var sortedContributors = contributors.ToArray<string>();
                Array.Sort(sortedContributors);
                summaryOutput.AppendLine("=========== External Contributors ===========");
                int externalCount = 0;
                foreach (string login in sortedContributors)
                {
                    if (!KnownInternalContributors.Contains(login))
                    {
                        externalCount++;
                        summaryOutput.AppendLine(login);
                    }
                }
                summaryOutput.AppendLine("=============================================");
                summaryOutput.AppendLine("Total external\t" + externalCount);
                summaryOutput.AppendLine("=============================================");
                summaryOutput.AppendLine();

                int internalCount = 0;
                summaryOutput.AppendLine("=========== Internal Contributors ===========");
                foreach (string login in sortedContributors)
                {
                    if (KnownInternalContributors.Contains(login))
                    {
                        internalCount++;
                        summaryOutput.AppendLine(login);
                    }
                }
                int totalContributorsCount = externalCount + internalCount;
                summaryOutput.AppendLine("=============================================");
                summaryOutput.AppendLine("Total internal\t" + internalCount);
                summaryOutput.AppendLine("=============================================");
                summaryOutput.AppendLine("Total contributors\t" + totalContributorsCount);
                summaryOutput.AppendLine("=============================================");
                summaryOutput.AppendLine();

                int TotalComments = stats[ContributionType.ExternalComment] + stats[ContributionType.InternalComment];
                int TotalIssues = stats[ContributionType.ExternalIssue] + stats[ContributionType.InternalIssue];
                int TotalPulls = stats[ContributionType.ExternalPull] + stats[ContributionType.InternalPull];

                summaryOutput.AppendLine("============= Contributions =================");
                summaryOutput.AppendLine("Contribution\tExternal\tInternal\tTotal");
                summaryOutput.AppendLine($"Comment\t" +
                    $"{stats[ContributionType.ExternalComment]}\t" +
                    $"{stats[ContributionType.InternalComment]}\t" +
                    $"{(stats[ContributionType.ExternalComment] + stats[ContributionType.InternalComment])}");
                summaryOutput.AppendLine($"Issue\t" +
                    $"{stats[ContributionType.ExternalIssue]}\t" +
                    $"{stats[ContributionType.InternalIssue]}\t" +
                    $"{(stats[ContributionType.ExternalIssue] + stats[ContributionType.InternalIssue])}");
                summaryOutput.AppendLine($"Pull\t" +
                    $"{stats[ContributionType.ExternalPull]}\t" +
                    $"{stats[ContributionType.InternalPull]}\t" +
                    $"{(stats[ContributionType.ExternalPull] + stats[ContributionType.InternalPull])}");
                summaryOutput.AppendLine("=============================================");
                summaryOutput.AppendLine($"TOTAL\t" +
                    $"{stats[ContributionType.ExternalComment] + stats[ContributionType.ExternalIssue] + stats[ContributionType.ExternalPull]}\t" +
                    $"{stats[ContributionType.InternalComment] + stats[ContributionType.InternalIssue] + stats[ContributionType.InternalPull]}\t" +
                    $"{TotalComments + TotalIssues + TotalPulls}\t");
                summaryOutput.AppendLine("=============================================");
                summaryOutput.AppendLine();

                summaryOutput.AppendLine("================= Summary ===================");
                summaryOutput.AppendLine(
                    "Date\t" +
                    "External Contributors\t" +
                    "Internal Contributors\t" +
                    "Total Contributors\t" +
                    "External Comments\t" +
                    "Internal Comments\t" +
                    "Total Comments\t" +
                    "External Issues\t" +
                    "Internal Issues\t" +
                    "Total Issues\t" +
                    "External Pull\t" +
                    "Internal Pull\t" +
                    "Total Pulls");
                string summaryStatsLine = $"{fromDate.Month}-{fromDate.Year}\t" +
                    $"{externalCount}\t" +
                    $"{internalCount}\t" +
                    $"{totalContributorsCount}\t" +
                    $"{stats[ContributionType.ExternalComment]}\t" +
                    $"{stats[ContributionType.InternalComment]}\t" +
                    $"{TotalComments}\t" +
                    $"{stats[ContributionType.ExternalIssue]}\t" +
                    $"{stats[ContributionType.InternalIssue]}\t" +
                    $"{TotalIssues}\t" +
                    $"{stats[ContributionType.ExternalPull]}\t" +
                    $"{stats[ContributionType.InternalPull]}\t" +
                    $"{TotalPulls}\n";
                summaryOutput.AppendLine(summaryStatsLine);
                summaryOutput.AppendLine("=============================================");

                Console.Write(summaryOutput.ToString());
                fileName = "issues_" + fromDate.Year + "-" + fromDate.Month + "-" + fromDate.Day + "_" + toDate.Year + "-" + toDate.Month + "-" + toDate.Day + "_summary.txt";
                File.WriteAllText(fileName, summaryOutput.ToString());
                File.AppendAllText("stats_summary.txt", summaryStatsLine);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.Message);
            }
        }

        private static void Usage()
        {
            Console.WriteLine("Usage: GetUniqueContributors.exe from_date to_date\nExample: GetUniqueContributors.exe 2020-02-01 2020-02-29");
        }
    }
}
