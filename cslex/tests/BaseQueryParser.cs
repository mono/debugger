using System;
using System.Data;
using System.Reflection;
using System.Collections;

namespace Microsoft.ObjectSpaces {
    internal class BaseQueryParser {
        int iAlias;
        private ObjectSpace os;

        private MapHelper helper;
        internal  ParserNode parseTree = null;
        QueryParserResult result;

        ObjectRelationalMap map;
        public void Setup(ObjectSpace os) {
            this.os = os;

            this.map = os.Map;
            this.helper = new MapHelper(this.map);
        }

        internal QueryParserResult GetParserResult( Type type) {
            iAlias = 0;

            result.table = new TableReference( os.GetTable(type), helper, NextAlias() );
            result.filter = FilterExpression.Combine( TranslateFilter( parseTree, result.table, type ), GetHeirarchyRestriction( type, result.table )  );
            result.type = type;

            return result;
        }

        private FilterExpression TranslateConstant( ValueNode constant ) {
            return new ValueExpression( constant.Value );
        }

        private FilterExpression TranslateUnary( 
            UnaryOperatorType uop, FilterExpression exp ) {
            
            RelatedColumnExpression exx = exp as RelatedColumnExpression;

            if( exx != null ) {
                return new ExistsExpression(
                    RelationQuery.Merge( 
                        (RelationQuery) exx.Source,
                        new UnaryOperatorExpression( uop, new ColumnExpression( exx ) ) 
                    )
                );
            }
            else {
                return new UnaryOperatorExpression( uop, exp );
            }
        }

        private FilterExpression TranslateFilter( ParserNode node, QueryExpression source, Type baseType ) {
            switch( node.nodeType ) {
                case ParserNodeType.Property:
                    return TranslateColumn( (PropertyNode) node, source, baseType );

                case ParserNodeType.Filter:
                    return TranslateExists( (FilterNode) node, source, baseType );
                    
                case ParserNodeType.Value:
                    return TranslateConstant( (ValueNode) node );

                case ParserNodeType.Binary:
                    BinaryNode bn = node as BinaryNode;
                    return TranslateBinary(bn.Operator, TranslateNode(bn.Left, source, baseType), TranslateNode(bn.Right, source, baseType));

                case ParserNodeType.Unary:
                    UnaryNode un = node as UnaryNode;
                    return TranslateUnary(un.Operator, TranslateNode(un.Operand, source, baseType) );

                default:
                    throw ExceptionBuilder.QueryParser_InvalidXPath();
            }
        }

        private FilterExpression TranslateExists( FilterNode filter, QueryExpression source, Type baseType ) {
            return new ExistsExpression( TranslateFilterQuery( filter, source, baseType ) );
        }

        private FilterExpression TranslateNode(ParserNode node, QueryExpression source, Type baseType) {
            switch (node.nodeType) {
                case ParserNodeType.Property:
                    return TranslateColumn(node, source, baseType);
            
                case ParserNodeType.Value:
                    return new ValueExpression(((ValueNode)node).Value);

                case ParserNodeType.Binary:
                    BinaryNode bn = node as BinaryNode;
                    return TranslateBinary(bn.Operator, TranslateNode(bn.Left, source, baseType), TranslateNode(bn.Right, source, baseType));

                case ParserNodeType.Unary:
                    UnaryNode un = node as UnaryNode;
                    return TranslateUnary(un.Operator, TranslateNode(un.Operand, source, baseType) );

                case ParserNodeType.Filter:
                    return TranslateFilter(node, source, baseType);
            }
            return null;
        }

        private Type GetPropertyType( Type type, string propertyName ) {
            PropertyInfo pi = type.GetProperty( propertyName );
            if( pi != null ) {
                return pi.PropertyType;
            }
            throw ExceptionBuilder.QueryParser_MissingProperty(type.Name, propertyName);
        }

        private Type GetPropertyResultType( Type type, string propertyName ){
            PropertyInfo pi = type.GetProperty( propertyName );
            pi = helper.GetAlias(type, pi);
            if( pi != null ) {
                if( helper.HasPersistentCollectionReturnType( pi ) ) {
                    return helper.GetPersistentReturnType( pi );
                }
                return pi.PropertyType;
            }
            throw ExceptionBuilder.QueryParser_MissingProperty(type.Name, pi.Name);
        }

        private PropertyMap GetPropertyMap( Type type, string propertyName ) {
            if( map != null ) {
                TypeMap tmap = map.TypeMaps[type.Name];
                if( tmap != null ) {
                    PropertyMap pmap = tmap.MemberMaps[propertyName] as PropertyMap;
                    return pmap;
                }
            }
            return null;
        }

        private FilterExpression TranslateColumn( ParserNode n, QueryExpression source, Type baseType ) {

            PropertyNode node = (PropertyNode) n;
            if( node.PropName == "" ) {
                // throw here?!?
                return null;
            }

            Type sourceType = baseType;

            if( node.Parent != null )
                sourceType = GetResultType( node.Parent, baseType );

            PropertyInfo pi = sourceType.GetProperty(node.PropName);                

            if (pi==null)
                throw ExceptionBuilder.QueryParser_MissingProperty(sourceType.ToString(), node.PropName);
            pi = helper.GetAlias(sourceType, pi);

            string columnName = helper.GetDataColumnName(pi);
            DataColumn column = os.GetTable( sourceType ).Columns[columnName];
            if( column != null && column.ColumnMapping != MappingType.Hidden ) {
                if( node.Parent != null ) {
                    return new RelatedColumnExpression( 
                        (RelationQuery) TranslateQuery( node.Parent, source, baseType ), 
                        column,
                        helper,
                        pi.PropertyType
                    );
                }
                else {
                    return new ColumnExpression( source, column, helper, pi.PropertyType );
                }
            }
            else {
                // not a column reference at all.. 
                return new ExistsExpression( TranslateQuery( n, source, baseType ) );
            }
        }

        private QueryExpression TranslateQuery( ParserNode node, QueryExpression source, Type baseType ) {
            switch( node.nodeType ) {
                case ParserNodeType.Property: 
                    return TranslatePropertyQuery( (PropertyNode) node, source, baseType );

                case ParserNodeType.Filter:
                    return TranslateFilterQuery( (FilterNode) node, source, baseType );

                default:
                    throw ExceptionBuilder.QueryParser_InvalidNodeType("enzo supply an error here");
            }
        }

        private Type GetResultType( ParserNode node, Type baseType ) {
            switch( node.nodeType ) {
                case ParserNodeType.Property: {
                    PropertyNode pNode = (PropertyNode)node;
                    if( pNode.Parent != null ) {
                        baseType = GetResultType(pNode.Parent, baseType);
                    }
                    Type resultType = GetPropertyResultType( baseType, pNode.PropName );
                    if( !resultType.IsAbstract ) {
                        throw ExceptionBuilder.QueryParser_PrePresentRelations();
                    }
                    return resultType;
                }

                case ParserNodeType.Filter:
                    return GetResultType( ((FilterNode)node).left, baseType );

                default:
                    throw ExceptionBuilder.QueryParser_InvalidNodeType(node.nodeType.ToString());
            }
        }

        private QueryExpression TranslateFilterQuery( FilterNode filter, QueryExpression source, Type baseType ) {
            if( filter.left != null ) {
                source = TranslateQuery( filter.left, source, baseType );
                baseType = GetResultType( filter.left, baseType );

                return new RelationQuery(
                    (RelationQuery) source,
                    TranslateFilter( filter.filter, new SourceReference(source), baseType )
                );
            }
            throw ExceptionBuilder.QueryParser_FiltersAxis();
        }

        private QueryExpression TranslatePropertyQuery( PropertyNode node, QueryExpression source, Type baseType ) {
            string propName = node.PropName;

            if( node.Parent != null ) {
                source = TranslateQuery( node.Parent, source, baseType );
                baseType = GetResultType( node.Parent, baseType );
            }

            DataRelation relation = os.GetRelation( baseType, propName );
            if( relation == null ) {
                throw ExceptionBuilder.QueryParser_InvalidProperty(propName,baseType.Name);
            }

            Type propType = GetPropertyType( baseType, propName );
            Type resultType = GetPropertyResultType( baseType, propName );
            RelationSide side = RelationSide.Parent;

            RelationPropertyMap map = GetPropertyMap( baseType, propName ) as RelationPropertyMap;
            if( map != null ) {
                side = map.Side;
            }
            else {
                if( typeof(IList).IsAssignableFrom(propType) ) {
                    side = RelationSide.Parent;
                }
                else {
                    side = RelationSide.Child;
                }
            }

            return new RelationQuery( 
                new TableReference( os.GetTable(resultType), helper, NextAlias() ),
                source,
                relation,
                side 
            );
        }

        private string NextAlias() {
            return "t" + (iAlias++);
        }

        private FilterExpression TranslateBinary( 
            BinaryOperatorType bop, object left, object right) {
            
            RelatedColumnExpression lex = left as RelatedColumnExpression;
            RelatedColumnExpression rex = right as RelatedColumnExpression;

            if( lex != null && rex == null ) {

                return new ExistsExpression(
                    RelationQuery.Merge( 
                        (RelationQuery) lex.Source, 
                        new BinaryOperatorExpression( bop, new ColumnExpression( lex ), (FilterExpression) right ) 
                    )
                );
            }
            else if( lex == null && rex != null ) {

                return new ExistsExpression(
                    RelationQuery.Merge( 
                        (RelationQuery) rex.Source, 
                        new BinaryOperatorExpression( bop, (FilterExpression) left, new ColumnExpression( rex ) ) 
                    )
                );
            }
            else if ( lex != null && rex != null ) {
                // dual exists expression
                throw ExceptionBuilder.QueryParser_DoubleExists();
            }
            else {
                return new BinaryOperatorExpression( bop, (FilterExpression) left,(FilterExpression)  right );
            }
        }

        private FilterExpression GetHeirarchyRestriction( Type type, TableReference table ) {
            FilterExpression x = null;
            Type[] types = helper.GetHeirarchyGroup(type);
            if( types != null ) {
                string sharedColumnName = helper.GetSharedColumn(type);
                DataColumn column = os.GetTable(type).Columns[sharedColumnName];
                foreach( Type t in types ) {
                    FilterExpression e = new BinaryOperatorExpression( 
                        BinaryOperatorType.Equality,
                        new ColumnExpression( table, column, helper, typeof(string) ), 
                        new ValueExpression( helper.GetSharedColumnValue(t) ) 
                    );
                    if( x != null ) {
                        x = new BinaryOperatorExpression( BinaryOperatorType.BooleanOr, x, e );
                    }
                    else {
                        x = e;
                    }
                }
            }
            return x;
        }

    };

}