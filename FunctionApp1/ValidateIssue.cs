using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Octokit;
using GraphQL.Client;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using GraphQL;
using System.Collections.Generic;
using System.Text.Json;

namespace ValidateIssue
{
    public static class ValidateIssue
    {
        private static string auth_token = "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";
        public class IssueResonse
        {
            public RepositoryOwnerType RepositoryOwner { get; set; }
            
            public class RepositoryOwnerType
            {
                public RepositoryType Repository { get; set; }

                public class RepositoryType
                {
                    public PullRequestType PullRequest { get; set; }

                    public class PullRequestType
                    {
                        public TimelineItemsType TimelineItems { get; set; }

                        public class TimelineItemsType
                        {
                            public int FilteredCount { get; set; }

                            public List<NodesType> Nodes { get; set; }

                            public class NodesType
                            {
                                public string __Typename { get; set; }

                                public SubjectType Subject { get; set; }

                                public class SubjectType
                                {
                                    public int Number { get; set; }
                                }
                            }
                        }
                    }
                }
            }
        }

        [FunctionName("ValidateIssue")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {

            log.LogInformation($"Webhook was triggered!");
            
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            
            
            int prNumber = data?.number;
            string owner = data?.user?.login;
            string repo = data?.head?.repo?.name;
            
            var client = new GraphQLHttpClient("https://api.github.com/graphql", new NewtonsoftJsonSerializer());
                
            client.HttpClient.DefaultRequestHeaders.Add("Authorization", $"bearer {auth_token}");
           // client.HttpClient.DefaultRequestHeaders.Add("User-Agent", owner);

            var issueRequest = new GraphQLRequest {
                Query = @"
               query {
                  repositoryOwner(login:""" + owner + @""") {
                    repository(name: """ + repo + @""") {
                      pullRequest(number: " + prNumber + @") {
                        timelineItems(itemTypes: [CONNECTED_EVENT, DISCONNECTED_EVENT], first: 100) {
                       filteredCount
                        nodes {
                          ... on ConnectedEvent {
                            __typename
                            subject {
                              ... on Issue {
                                number
                              }
                            }
                          }
                          ... on DisconnectedEvent {
                            __typename
                            id
                            subject {
                              ... on Issue {
                                number
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
                "
            };
            /* Debug info
            Console.WriteLine(issueRequest.Query);
            Console.WriteLine("And the Json is: \n\n");
            */

            var graphQLResponse = await client.SendQueryAsync<IssueResonse>(issueRequest);
            
            /*Debug info
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(graphQLResponse, new JsonSerializerOptions { WriteIndented = true }));
            */

            int size = graphQLResponse.Data.RepositoryOwner.Repository.PullRequest.TimelineItems.FilteredCount;

            //Send to github
            var productHeader = new ProductHeaderValue("MyAmazingApp", auth_token);
            var tokenAuth = new Credentials(auth_token);
            var githubClient = new GitHubClient(productHeader);
            githubClient.Credentials = tokenAuth;
            

            string responseMessage;
            if (size == 0)
            {
                responseMessage = "Rep is new? Issues Comment is null";
                return new BadRequestObjectResult(responseMessage);
            }

            IssueResonse.RepositoryOwnerType.RepositoryType.PullRequestType.TimelineItemsType.NodesType[] arr = graphQLResponse.Data.RepositoryOwner.Repository.PullRequest.TimelineItems.Nodes.ToArray();
            string lastTag= arr[size - 1].__Typename;
            
            /*Debug info
            Console.WriteLine(lastTag);
            */

            if (lastTag.Equals("ConnectedEvent"))
            {
                var responce = await githubClient.Issue.Comment.Create(owner, repo, prNumber, $"Issue is implement");
                responseMessage = "Issue is implement";
            }
            else
            {
                var responce = await githubClient.Issue.Comment.Create(owner, repo, prNumber, $"Issue is NOT implement");
                responseMessage = "Issue is NOT implement";
            }

            return new OkObjectResult(responseMessage);
        }

        private static bool checkEventsListForConnectedEvent()
        {

            return false;
        }
    }
}
