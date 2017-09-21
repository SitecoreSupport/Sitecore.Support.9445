namespace Sitecore.Support.XA.Foundation.Search.Providers.Solr
{
    using Sitecore.ContentSearch;
    using Sitecore.ContentSearch.Abstractions;
    using Sitecore.ContentSearch.Boosting;
    using Sitecore.ContentSearch.SolrProvider;
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    public class CustomSolrDocumentBuilder : SolrDocumentBuilder
    {
        // Fields
        private readonly IProviderUpdateContext _context;
        private readonly CultureInfo _culture;
        private readonly SolrFieldNameTranslator _fieldNameTranslator;
        private readonly ISettings _settings;

        // Methods
        public CustomSolrDocumentBuilder(IIndexable indexable, IProviderUpdateContext context) : base(indexable, context)
        {
            this._context = context;
            this._fieldNameTranslator = context.Index.FieldNameTranslator as SolrFieldNameTranslator;
            this._culture = indexable.Culture;
            this._settings = context.Index.Locator.GetInstance<ISettings>();
        }

        public override void AddField(IIndexableDataField field)
        {
            string name = field.Name;
            object fieldValue = base.Index.Configuration.FieldReaders.GetFieldValue(field);
            AbstractSearchFieldConfiguration fieldConfiguration = this._context.Index.Configuration.FieldMap.GetFieldConfiguration(name);
            if (((fieldConfiguration != null) && (fieldValue == null)) && ((fieldConfiguration as SolrSearchFieldConfiguration).NullValue != null))
            {
                fieldValue = (fieldConfiguration as SolrSearchFieldConfiguration).NullValue;
            }
            if (((fieldValue is string) && (fieldConfiguration != null)) && ((((string)fieldValue) == string.Empty) && ((fieldConfiguration as SolrSearchFieldConfiguration).EmptyString != null)))
            {
                fieldValue = (fieldConfiguration as SolrSearchFieldConfiguration).EmptyString;
            }
            if (fieldValue == null)
            {
                VerboseLogging.CrawlingLogDebug(() => string.Format("Skipping field id:{0}, name:{1}, typeKey:{2} - Value is null.", field.Id, field.Name, field.TypeKey));
            }
            else if (string.IsNullOrWhiteSpace(fieldValue.ToString()))
            {
                VerboseLogging.CrawlingLogDebug(() => string.Format("Skipping field id:{0}, name:{1}, typeKey:{2} - Value is empty.", field.Id, field.Name, field.TypeKey));
            }
            else
            {
                float num = BoostingManager.ResolveFieldBoosting(field) + this.GetFieldConfigurationBoost(name);
                string fieldName = this._fieldNameTranslator.GetIndexFieldName(name, fieldValue.GetType(), this._culture);
                if (!base.IsMedia && IndexOperationsHelper.IsTextField(field))
                {
                    this.StoreField(BuiltinFields.Content, BuiltinFields.Content, fieldValue, true, null, field.TypeKey);
                }
                this.StoreField(name, fieldName, fieldValue, false, new float?(num), field.TypeKey);
            }
        }

        private float GetFieldConfigurationBoost(string fieldName)
        {
            SolrSearchFieldConfiguration fieldConfiguration = this._context.Index.Configuration.FieldMap.GetFieldConfiguration(fieldName) as SolrSearchFieldConfiguration;
            if (fieldConfiguration != null)
            {
                return fieldConfiguration.Boost;
            }
            return 0f;
        }

        private void StoreField(string unTranslatedFieldName, string fieldName, object fieldValue, bool append = false, float? boost = new float?(), string returnType = null)
        {
            object obj = fieldValue;

            if (Index.Configuration.IndexFieldStorageValueFormatter != null)
            {
                fieldValue = Index.Configuration.IndexFieldStorageValueFormatter.FormatValueForIndexStorage(fieldValue, unTranslatedFieldName);
            }
            if (VerboseLogging.Enabled)
            {
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.AppendFormat("Field: {0}" + Environment.NewLine, fieldName);
                stringBuilder.AppendFormat(" - value: {0}{1}" + Environment.NewLine, obj?.GetType().ToString() ?? "NULL", obj is string || !(obj is IEnumerable) ? "" : (" - count : " + ((IEnumerable)obj).Cast<object>().Count()));
                stringBuilder.AppendFormat(" - unformatted value: {0}" + Environment.NewLine, obj ?? "NULL");
                stringBuilder.AppendFormat(" - formatted value:   {0}" + Environment.NewLine, fieldValue ?? "NULL");
                stringBuilder.AppendFormat(" - returnType: {0}" + Environment.NewLine, returnType);
                stringBuilder.AppendFormat(" - boost: {0}" + Environment.NewLine, boost);
                stringBuilder.AppendFormat(" - append: {0}" + Environment.NewLine, (append ? 1 : 0));
                VerboseLogging.CrawlingLogDebug(((object)stringBuilder).ToString);
            }

            if (append && Document.ContainsKey(fieldName) && fieldValue is string)
            {
                ConcurrentDictionary<string, object> document;
                string index;
                (document = Document)[index = fieldName] = (object)((string)document[index] + (object)" " + (string)fieldValue);
            }

            if (Document.ContainsKey(fieldName))
            {
                return;
            }

            if (boost.HasValue)
            {
                float? nullable = boost;
                if ((nullable.GetValueOrDefault() <= 0.0 ? 0 : (nullable.HasValue ? 1 : 0)) != 0) fieldValue = new SolrBoostedField(fieldValue, boost);
            }

            Document.GetOrAdd(fieldName, fieldValue);

            if (_fieldNameTranslator.HasCulture(fieldName))
            {
                Document.GetOrAdd(_fieldNameTranslator.StripKnownCultures(fieldName), fieldValue);
            }
        }
    }
}

