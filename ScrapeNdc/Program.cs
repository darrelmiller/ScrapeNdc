using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScrapeNdc
{
    class Program
    {
        static void Main(string[] args)
        {
            var client = new HttpClient();

            var scraper = new NdcScraper(client);

            scraper.LoadTopics().Wait();
            scraper.LoadSpeakers().Wait();
            

            scraper.LoadSessions(1,new Uri("http://ndclondon.oktaset.com/Agenda/wednesday")).Wait();
            scraper.LoadSessions(2, new Uri("http://ndclondon.oktaset.com/Agenda/thursday")).Wait();
            scraper.LoadSessions(3, new Uri("http://ndclondon.oktaset.com/Agenda/friday")).Wait();
            scraper.SaveData();
        }
    }

    public class NdcScraper
    {
        private readonly HttpClient _httpClient;
        private JArray _Speakers;
        private JArray _Topics;
        private JArray[] _Sessions = new JArray[3];
        private JArray _SessionTopics;

        public NdcScraper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task LoadSessions(int dayno, Uri sessionList)
        {
            var response = await _httpClient.GetAsync(sessionList);
            var dom = new HtmlDocument();
            dom.Load(await response.Content.ReadAsStreamAsync());

            var sessionNodes = dom.DocumentNode.Descendants("td").Where(e => e.Attributes["class"] != null && e.Attributes["class"].Value.Contains("session")).ToList();

            var sessions = new JArray();
            int id = 100 * dayno;
            foreach (var sessionNode in sessionNodes)
            {
                dynamic session = new JObject();
                session.id = id++;
                session.dayno = dayno;
                session.title = sessionNode.SelectSingleNode("div[@class='title']").InnerText;
                var speakerName = sessionNode.SelectSingleNode("div[@class='speaker']").InnerText;
                session.speakerId = FindSpeaker(speakerName);

                var tagnodes = sessionNode.SelectNodes(".//div[@class='tag']/span");
                if (tagnodes != null)
                {
                    foreach (var tagnode in tagnodes)
                    {
                        var topicId = FindTopic(tagnode.Attributes["title"].Value);
                        dynamic sessionTopic = new JObject();
                        sessionTopic.topicId = topicId;
                        sessionTopic.SessionId = id;
                        _SessionTopics.Add(sessionTopic);
                    }
                }
                sessions.Add(session);

            }
            _Sessions[dayno-1] = sessions;
        }

        private int FindSpeaker(string name)
        {
            dynamic speaker = _Speakers.Cast<JObject>().Where(s => ((string)s["name"]).StartsWith(name)).FirstOrDefault();
            if (speaker == null) return 0;
            return (int)speaker.id;
        }
        public async Task LoadSpeakers()
        {
            var speakersUri = new Uri("http://www.ndc-london.com/ndc_speakers");
            var response = await _httpClient.GetAsync(speakersUri);
            var dom = new HtmlDocument();
            dom.Load(await response.Content.ReadAsStreamAsync());

            var speakerNodes = dom.DocumentNode.SelectNodes("//div[@class='SpeakerWrapper']");

            int id = 1;
            var speakers = new JArray();
            foreach (var node in speakerNodes)
            {

                dynamic speaker = new JObject();
                speaker.id = id++;
                speaker.name = node.SelectSingleNode("h3").InnerText;
                speaker.bio =
                    node.SelectSingleNode("div[@class='descriptionOfSpeakerList']/div[@class='speakerPopupText']")
                        .InnerText;
                speaker.image_url = node.SelectSingleNode("div[@class='descriptionOfSpeakerList']/img").Attributes["src"].Value;
                speakers.Add(speaker);
            }

            _Speakers = speakers;
            

        }

        private int FindTopic(string name)
        {
            dynamic topic = _Topics.Cast<JObject>().FirstOrDefault(s => (string)s["name"] == name);
            if (topic == null) return 0;
            return (int)topic.id;
        }

        public async Task LoadTopics()
        {
            var topicsUri = new Uri("http://ndclondon.oktaset.com/Agenda/wednesday");
            var response = await _httpClient.GetAsync(topicsUri);
            var dom = new HtmlDocument();
            dom.Load(await response.Content.ReadAsStreamAsync());

            var topicsNode = dom.DocumentNode.SelectSingleNode("//ul[@id='tagLegend']");

            var topicNodes = topicsNode.SelectNodes("li");

            int id = 1;
            var topics = new JArray();
            foreach (var node in topicNodes)
            {
                dynamic topic = new JObject();
                topic.id = id++;
                topic.name = node.InnerText;
                topics.Add(topic);
            }

            _Topics = topics;

        }



        public void SaveData()
        {
            SaveData("speakers.json", _Speakers);
            SaveData("topics.json", _Topics);
            var allsessions = new JArray();
            foreach (var sessions in _Sessions)
            {
                foreach (var session in sessions)
                {
                    allsessions.Add(session);
                }
            }
            
            SaveData("sessions.json", allsessions);
            SaveData("sessiontopics.json", _SessionTopics);
        }

        private void SaveData(string speakersJson, JArray jsonData)
        {
            var stream = new FileStream(speakersJson, FileMode.Create, FileAccess.ReadWrite);
            var streamWriter = new StreamWriter(stream);
            var jdoc = new JsonTextWriter(streamWriter);
            jdoc.Formatting = Formatting.Indented;

            jsonData.WriteTo(jdoc);

            jdoc.Flush();
        }
    }
}
