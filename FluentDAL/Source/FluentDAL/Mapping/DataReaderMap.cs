using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Data;
using System.Reflection;
using System.Collections;

namespace FluentDAL.Mapping
{
    /// <summary>
    /// Base class for mapping between entities and IDataReaders
    /// </summary>
    public abstract class DataReaderMap<TEntity> : IDataReaderMap
        where TEntity : class, new()
    {
        #region Private properties

        private Dictionary<string, IPropertyMapping> _propertyMap;
        private Dictionary<PropertyInfo, IDataReaderMap> _referenceMap;

        private Dictionary<string, IPropertyMapping> PropertyMap
        {
            get 
            {
                if (_propertyMap == null)
                {
                    _propertyMap = new Dictionary<string, IPropertyMapping>();
                }
                return _propertyMap;
            }
        }

        private Dictionary<PropertyInfo, IDataReaderMap> ReferenceMap
        {
            get
            {
                if (_referenceMap == null)
                {
                    _referenceMap = new Dictionary<PropertyInfo, IDataReaderMap>();
                }
                return _referenceMap;
            }
        }

        #endregion

        #region Public instance methods

        /// <summary>
        /// Maps a property of an entity to the value from a column in a data reader
        /// </summary>
        /// <param name="expression">Expression resolving to propertyr to map</param>
        /// <param name="columnName">Data reader column name to map from</param>
        protected void Map(Expression<Func<TEntity, object>> expression, string columnName)
        {
            MemberExpression memberExpression = GetMemberExpression(expression);
            if (memberExpression != null)
            {
                PropertyInfo info = (PropertyInfo)memberExpression.Member;
                PropertyMapping mapping = new PropertyMapping(info);
                PropertyMap.Add(PrepareColumnName(columnName), mapping);
            }
            else
            {
                Log.Error("{0}: Error mapping expression '{1}' to column name '{2}'", this.GetType().Name, expression.ToString(), columnName);
            }
        }

        /// <summary>
        /// Maps a property of an entity to the value from a column in a data reader, via a conversion function
        /// </summary>
        /// <typeparam name="TInput">Type of input from data reader</typeparam>
        /// <typeparam name="TOutput">Type of output property</typeparam>
        /// <param name="expression">Expression resolving to propertyr to map</param>
        /// <param name="columnName">Data reader column name to map from</param>
        /// <param name="conversion">Function to execute conversion from database to entity format</param>
        protected void Map<TInput, TOutput>(Expression<Func<TEntity, object>> expression, string columnName, Func<TInput, TOutput> conversion)
        {
            MemberExpression memberExpression = GetMemberExpression(expression);
            if (memberExpression != null)
            {
                PropertyInfo info = (PropertyInfo)memberExpression.Member;
                PropertyMapping mapping = new PropertyMapping<TInput, TOutput>(info, conversion);
                PropertyMap.Add(PrepareColumnName(columnName), mapping);
            }
            else
            {
                Log.Error("{0}: Error mapping expression '{1}' to column name '{2}'", this.GetType().Name, expression.ToString(), columnName);
            }
        }       

        /// <summary>
        /// References a property to a composite entity loaded from another map
        /// </summary>
        /// <remarks>
        /// Use when a hierarchy of entities is loaded from the same data reader
        /// </remarks>
        /// <typeparam name="TOther">Type of entity referenced to</typeparam>
        /// <param name="expression">Expression resolving to propertyr to map</param>
        /// <param name="map">Map to populate referenced property</param>
        protected void References<TOther>(Expression<Func<TEntity, object>> expression, DataReaderMap<TOther> map)
            where TOther : class, new()
        {
            MemberExpression memberExpression = GetMemberExpression(expression);
            if (memberExpression != null)
            {
                PropertyInfo info = (PropertyInfo)memberExpression.Member;
                ReferenceMap.Add(info, map);
            }
            else
            {
                Log.Error("{0}: Error referencing expression '{1}' to map '{2}'", this.GetType().Name, expression.ToString(), map.GetType().Name);
            }
        }

        /// <summary>
        /// Populates the supplied entity from the current row of the data reader
        /// </summary>
        /// <remarks>
        /// Errors in mapping types from the reader to the entity are logged but no exceptions are thrown
        /// </remarks>
        /// <param name="entity">Entity to populate</param>
        /// <param name="sourceReader">Open data reader, positioned at start point for population</param>
        public void Populate(TEntity entity, IDataReader sourceReader)
        {
            Populate(entity, sourceReader, false);         
        }

        /// <summary>
        /// Populates the supplied entity from the current row of the data reader
        /// </summary>
        /// <param name="entity">Entity to populate</param>
        /// <param name="sourceReader">Open data reader, positioned at start point for population</param>
        /// <param name="throwOnError">Whether mapping errors throw exceptions</param>
        public void Populate(TEntity targetEntity, IDataReader sourceReader, bool throwOnError)
        {
            //get the index number of uppercased column names:
            Dictionary<string, int> columnNames = new Dictionary<string, int>(sourceReader.FieldCount);
            for (int columnIndex = 0; columnIndex < sourceReader.FieldCount; columnIndex++)
            {
                columnNames.Add(PrepareColumnName(sourceReader.GetName(columnIndex)), columnIndex);
            }

            //map the column values to the properties:
            foreach (KeyValuePair<string, IPropertyMapping> targetProperty in PropertyMap)
            {
                if (columnNames.ContainsKey(targetProperty.Key))
                {
                    object sourceValue = sourceReader.GetValue(columnNames[targetProperty.Key]);
                    if (sourceValue != null)
                    {
                        Type sourceType = sourceValue.GetType();
                        if (sourceType != typeof(DBNull))
                        {
                            SetPropertyValue(targetEntity, sourceValue, sourceType, targetProperty.Value, throwOnError);
                        }
                    }
                }
            }

            //and map references:
            foreach (KeyValuePair<PropertyInfo, IDataReaderMap> referenceProperty in ReferenceMap)
            {
                object referencedObject = referenceProperty.Value.Create(sourceReader);
                PropertyMapping mapping = new PropertyMapping(referenceProperty.Key);
                SetPropertyValue(targetEntity, referencedObject, referenceProperty.Value.TargetType, mapping, false);
            }
        }

        /// <summary>
        /// Creates and populates an entity from the contents of a data reader
        /// </summary>
        /// <param name="sourceReader">Open data reader, positioned at start point for population</param>
        /// <returns>Populated entity</returns>
        public TEntity Create(IDataReader sourceReader)
        {
            TEntity entity = new TEntity();
            Populate(entity, sourceReader);
            return entity;
        }

        /// <summary>
        /// Creates and populates an entity from the contents of a data reader
        /// </summary>
        /// <param name="sourceReader">Open data reader, positioned at start point for population</param>
        /// <param name="throwOnError">Whether mapping errors throw exceptions</param>
        /// <returns>Populated entity</returns>
        public TEntity Create(IDataReader sourceReader, bool throwOnError)
        {
            TEntity entity = new TEntity();
            Populate(entity, sourceReader, throwOnError);
            return entity;
        }

        /// <summary>
        /// Populates a list of entities from the contents of a data reader
        /// </summary>
        /// <param name="list">List to populate</param>
        /// <param name="sourceReader">Open data reader</param>
        public void PopulateList(IList list, IDataReader sourceReader)
        {
            while (sourceReader.Read())
            {
                list.Add(Create(sourceReader));
            }
        }

        /// <summary>
        /// Creates and populates a list of entities from the contents of a data reader
        /// </summary>
        /// <typeparam name="TList">Type of entity list</typeparam>
        /// <param name="sourceReader">Open data reader</param>
        /// <returns>Populated list of entities</returns>
        public TList CreateList<TList>(IDataReader sourceReader)
            where TList : IList, new()
        {
            TList list = new TList();
            while (sourceReader.Read())
            {
                list.Add(Create(sourceReader));
            }
            return list;
        }

        #endregion

        #region Private methods

        private void SetPropertyValue(object targetEntity, object sourceValue, Type sourceType, IPropertyMapping mapping, bool throwOnError)
        {
            try
            {
                object targetValue = sourceValue;
                if (mapping.HasConversion)
                {
                    targetValue = mapping.Convert(sourceValue);
                }
                else if (mapping.PropertyInfo.PropertyType != sourceType && !IsNullable(mapping.PropertyInfo.PropertyType))
                {
                    //if the types differ, change them - unless the target is nullable, 
                    //as the Convert will fail but the SetValue succeeds:
                    targetValue = System.Convert.ChangeType(sourceValue, mapping.PropertyInfo.PropertyType);
                }
                mapping.PropertyInfo.SetValue(targetEntity, targetValue, null);
            }
            catch (Exception ex)
            {
                Log.Error("{0}: Error setting property value, {1}, {2}", this.GetType().Name, mapping.PropertyInfo.Name, ex.Message);
                if (throwOnError)
                {
                    throw ex;
                }
            }
        }

        private static string PrepareColumnName(string columnName)
        {
            return columnName.Trim().ToUpper();
        }

        private static MemberExpression GetMemberExpression(Expression<Func<TEntity, object>> expression)
        {
            MemberExpression memberExpression = null;
            if (expression.Body.NodeType == ExpressionType.Convert)
            {
                var body = (UnaryExpression)expression.Body;
                memberExpression = body.Operand as MemberExpression;
            }
            else if (expression.Body.NodeType == ExpressionType.MemberAccess)
            {
                memberExpression = expression.Body as MemberExpression;
            }

            return memberExpression;
        }

        private static bool IsNullable(Type type)
        {
            return (type.IsGenericType && type.GetGenericTypeDefinition().Equals(typeof(Nullable<>)));
        }

        #endregion

        #region IDataReaderMap

        Type IDataReaderMap.TargetType
        {
            get { return typeof(TEntity); }
        }

        void IDataReaderMap.Populate(object entity, IDataReader reader)
        {
            this.Populate((TEntity)entity, reader);
        }

        object IDataReaderMap.Create(IDataReader reader)
        {
            return (object)this.Create(reader);
        }

        #endregion
    }
}
