namespace Vidyano.ViewModel
{
    /// <summary>
    /// Allows customizing query column behavior at request time, enabling dynamic control over
    /// data filtering (inclusion/exclusion of values) and content truncation.
    /// </summary>
    public sealed class ColumnOverride
    {
        public ColumnOverride(string name)
        {
            Name = name ?? throw new System.ArgumentNullException(nameof(name));
        }

        /// <summary>
        /// Gets the name of the query column to override.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets or sets the values to include in query results. When set, only these values will be returned for the column.
        /// </summary>
        public string[] Includes { get; set; }

        /// <summary>
        /// Gets or sets the values to exclude from query results.
        /// </summary>
        public string[] Excludes { get; set; }

        /// <summary>
        /// Gets or sets whether to include full content of text columns without truncation.
        /// If the column already has a QueryMaxContentLength hint, this property will be ignored.
        /// </summary>
        public bool IncludeAllContent { get; set; }
    }
}
