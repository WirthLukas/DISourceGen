using System;

namespace DISourceGen
{
    public class SourceGenException : Exception
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string MessageFormat { get; set; }
        public string Category { get; set; }

        public SourceGenException(string id, string title, string messageFormat, string category)
        {
            Id = id;
            Title = title;
            MessageFormat = messageFormat;
            Category = category;
        }
    }
}
