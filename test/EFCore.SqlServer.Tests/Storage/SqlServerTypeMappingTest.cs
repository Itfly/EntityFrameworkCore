// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Storage
{
    public class SqlServerTypeMappingTest : RelationalTypeMappingTest
    {
        [Theory]
        [InlineData(nameof(ChangeTracker.DetectChanges), false)]
        [InlineData(nameof(PropertyEntry.CurrentValue), false)]
        [InlineData(nameof(PropertyEntry.OriginalValue), false)]
        [InlineData(nameof(ChangeTracker.DetectChanges), true)]
        [InlineData(nameof(PropertyEntry.CurrentValue), true)]
        [InlineData(nameof(PropertyEntry.OriginalValue), true)]
        public void Row_version_is_marked_as_modified_only_if_it_really_changed(string mode, bool changeValue)
        {
            using (var context = new OptimisticContext())
            {
                var token = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
                var newToken = changeValue ? new byte[] { 1, 2, 3, 4, 0, 6, 7, 8 } : token;

                var entity = context.Attach(
                    new WithRowVersion
                    {
                        Id = 789,
                        Version = token.ToArray()
                    }).Entity;

                var propertyEntry = context.Entry(entity).Property(e => e.Version);

                Assert.Equal(token, propertyEntry.CurrentValue);
                Assert.Equal(token, propertyEntry.OriginalValue);
                Assert.False(propertyEntry.IsModified);
                Assert.Equal(EntityState.Unchanged, context.Entry(entity).State);

                switch (mode)
                {
                    case nameof(ChangeTracker.DetectChanges):
                        entity.Version = newToken.ToArray();
                        context.ChangeTracker.DetectChanges();
                        break;
                    case nameof(PropertyEntry.CurrentValue):
                        propertyEntry.CurrentValue = newToken.ToArray();
                        break;
                    case nameof(PropertyEntry.OriginalValue):
                        propertyEntry.OriginalValue = newToken.ToArray();
                        break;
                    default:
                        throw new NotImplementedException("Unexpected test mode.");
                }

                Assert.Equal(changeValue, propertyEntry.IsModified);
                Assert.Equal(changeValue ? EntityState.Modified : EntityState.Unchanged, context.Entry(entity).State);
            }
        }

        private class WithRowVersion
        {
            public int Id { get; set; }
            public byte[] Version { get; set; }
        }

        private class OptimisticContext : DbContext
        {
            public DbSet<WithRowVersion> _ { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseSqlServer("Data Source=Branston");

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<WithRowVersion>().Property(e => e.Version).IsRowVersion();
            }
        }

        protected override DbCommand CreateTestCommand()
            => new SqlCommand();

        protected override DbType DefaultParameterType
            => DbType.Int32;

        [InlineData(typeof(SqlServerDateTimeOffsetTypeMapping), typeof(DateTimeOffset))]
        [InlineData(typeof(SqlServerDateTimeTypeMapping), typeof(DateTime))]
        [InlineData(typeof(SqlServerDoubleTypeMapping), typeof(double))]
        [InlineData(typeof(SqlServerFloatTypeMapping), typeof(float))]
        [InlineData(typeof(SqlServerTimeSpanTypeMapping), typeof(TimeSpan))]
        public override void Create_and_clone_with_converter(Type mappingType, Type clrType)
        {
            base.Create_and_clone_with_converter(mappingType, clrType);
        }

        [InlineData(typeof(SqlServerByteArrayTypeMapping), typeof(byte[]))]
        public override void Create_and_clone_sized_mappings_with_converter(Type mappingType, Type clrType)
        {
            base.Create_and_clone_sized_mappings_with_converter(mappingType, clrType);
        }

        [InlineData(typeof(SqlServerStringTypeMapping), typeof(string))]
        public override void Create_and_clone_unicode_sized_mappings_with_converter(Type mappingType, Type clrType)
        {
            base.Create_and_clone_unicode_sized_mappings_with_converter(mappingType, clrType);
        }

        [Fact]
        public virtual void Create_and_clone_UDT_mapping_with_converter()
        {
            var mapping = new SqlServerUdtTypeMapping(
                "storeType",
                typeof(object),
                "udtType",
                new FakeValueConverter(),
                new FakeValueComparer(),
                new FakeValueComparer(),
                DbType.VarNumeric,
                false,
                33,
                true);

            var clone = (SqlServerUdtTypeMapping)mapping.Clone("<clone>", 66);

            Assert.NotSame(mapping, clone);
            Assert.Same(mapping.GetType(), clone.GetType());
            Assert.Equal("storeType", mapping.StoreType);
            Assert.Equal("<clone>", clone.StoreType);
            Assert.Equal("udtType", mapping.UdtTypeName);
            Assert.Equal("udtType", clone.UdtTypeName);
            Assert.Equal(DbType.VarNumeric, clone.DbType);
            Assert.Equal(33, mapping.Size);
            Assert.Equal(66, clone.Size);
            Assert.False(mapping.IsUnicode);
            Assert.False(clone.IsUnicode);
            Assert.NotNull(mapping.Converter);
            Assert.Same(mapping.Converter, clone.Converter);
            Assert.Same(mapping.Comparer, clone.Comparer);
            Assert.Same(mapping.KeyComparer, clone.KeyComparer);
            Assert.Same(typeof(object), clone.ClrType);
            Assert.True(mapping.IsFixedLength);
            Assert.True(clone.IsFixedLength);

            var newConverter = new FakeValueConverter();
            clone = (SqlServerUdtTypeMapping)mapping.Clone(newConverter);

            Assert.NotSame(mapping, clone);
            Assert.Same(mapping.GetType(), clone.GetType());
            Assert.Equal("storeType", mapping.StoreType);
            Assert.Equal("storeType", clone.StoreType);
            Assert.Equal("udtType", mapping.UdtTypeName);
            Assert.Equal("udtType", clone.UdtTypeName);
            Assert.Equal(DbType.VarNumeric, clone.DbType);
            Assert.Equal(33, mapping.Size);
            Assert.Equal(33, clone.Size);
            Assert.False(mapping.IsUnicode);
            Assert.False(clone.IsUnicode);
            Assert.NotSame(mapping.Converter, clone.Converter);
            Assert.Same(mapping.Comparer, clone.Comparer);
            Assert.Same(mapping.KeyComparer, clone.KeyComparer);
            Assert.Same(typeof(object), clone.ClrType);
            Assert.True(mapping.IsFixedLength);
            Assert.True(clone.IsFixedLength);
        }

        public static RelationalTypeMapping GetMapping(
            Type type)
            => (RelationalTypeMapping)new SqlServerTypeMappingSource(
                    TestServiceFactory.Instance.Create<TypeMappingSourceDependencies>(),
                    TestServiceFactory.Instance.Create<RelationalTypeMappingSourceDependencies>())
                .FindMapping(type);

        public override void GenerateSqlLiteral_returns_ByteArray_literal()
        {
            var value = new byte[] { 0xDA, 0x7A };
            var literal = GetMapping(typeof(byte[])).GenerateSqlLiteral(value);
            Assert.Equal("0xDA7A", literal);
        }

        public override void GenerateSqlLiteral_returns_DateTime_literal()
        {
            var value = new DateTime(2015, 3, 12, 13, 36, 37, 371);
            var literal = GetMapping(typeof(DateTime)).GenerateSqlLiteral(value);

            Assert.Equal("'2015-03-12T13:36:37.371'", literal);
        }

        public override void GenerateSqlLiteral_returns_DateTimeOffset_literal()
        {
            var value = new DateTimeOffset(2015, 3, 12, 13, 36, 37, 371, new TimeSpan(-7, 0, 0));
            var literal = GetMapping(typeof(DateTimeOffset)).GenerateSqlLiteral(value);

            Assert.Equal("'2015-03-12T13:36:37.371-07:00'", literal);
        }

        [Fact]
        public virtual void GenerateSqlLiteralValue_returns_Unicode_String_literal()
        {
            var mapping = new SqlServerTypeMappingSource(
                    TestServiceFactory.Instance.Create<TypeMappingSourceDependencies>(),
                    TestServiceFactory.Instance.Create<RelationalTypeMappingSourceDependencies>())
                .FindMapping("nvarchar(max)");

            var literal = mapping.GenerateSqlLiteral("A Unicode String");

            Assert.Equal("N'A Unicode String'", literal);
        }

        [Fact]
        public virtual void GenerateSqlLiteralValue_returns_NonUnicode_String_literal()
        {
            var mapping = new SqlServerTypeMappingSource(
                    TestServiceFactory.Instance.Create<TypeMappingSourceDependencies>(),
                    TestServiceFactory.Instance.Create<RelationalTypeMappingSourceDependencies>())
                .FindMapping("varchar(max)");

            var literal = mapping.GenerateSqlLiteral("A Non-Unicode String");
            Assert.Equal("'A Non-Unicode String'", literal);
        }

        [Theory]
        [InlineData("Microsoft.SqlServer.Types.SqlHierarchyId", "hierarchyid")]
        [InlineData("Microsoft.SqlServer.Types.SqlGeography", "geography")]
        [InlineData("Microsoft.SqlServer.Types.SqlGeometry", "geometry")]
        public virtual void Get_named_mappings_for_sql_type(string typeName, string udtName)
        {
            var mapper = (IRelationalTypeMappingSource)new SqlServerTypeMappingSource(
                TestServiceFactory.Instance.Create<TypeMappingSourceDependencies>(),
                TestServiceFactory.Instance.Create<RelationalTypeMappingSourceDependencies>());

            var type = new FakeType(typeName);

            var mapping = mapper.FindMapping(type);

            Assert.Equal(udtName, mapping.StoreType);
            Assert.Equal(udtName, ((SqlServerUdtTypeMapping)mapping).UdtTypeName);
            Assert.Same(type, mapping.ClrType);
        }

        private class FakeType : Type
        {
            public FakeType(string fullName)
            {
                FullName = fullName;
            }

            public override object[] GetCustomAttributes(bool inherit) => throw new NotImplementedException();
            public override bool IsDefined(Type attributeType, bool inherit) => throw new NotImplementedException();
            public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr) => throw new NotImplementedException();
            public override Type GetInterface(string name, bool ignoreCase) => throw new NotImplementedException();
            public override Type[] GetInterfaces() => throw new NotImplementedException();
            public override EventInfo GetEvent(string name, BindingFlags bindingAttr) => throw new NotImplementedException();
            public override EventInfo[] GetEvents(BindingFlags bindingAttr) => throw new NotImplementedException();
            public override Type[] GetNestedTypes(BindingFlags bindingAttr) => throw new NotImplementedException();
            public override Type GetNestedType(string name, BindingFlags bindingAttr) => throw new NotImplementedException();
            public override Type GetElementType() => throw new NotImplementedException();
            protected override bool HasElementTypeImpl() => throw new NotImplementedException();
            protected override PropertyInfo GetPropertyImpl(string name, BindingFlags bindingAttr, Binder binder, Type returnType, Type[] types, ParameterModifier[] modifiers) => throw new NotImplementedException();
            public override PropertyInfo[] GetProperties(BindingFlags bindingAttr) => throw new NotImplementedException();
            protected override MethodInfo GetMethodImpl(string name, BindingFlags bindingAttr, Binder binder, CallingConventions callConvention, Type[] types, ParameterModifier[] modifiers) => throw new NotImplementedException();
            public override MethodInfo[] GetMethods(BindingFlags bindingAttr) => throw new NotImplementedException();
            public override FieldInfo GetField(string name, BindingFlags bindingAttr) => throw new NotImplementedException();
            public override FieldInfo[] GetFields(BindingFlags bindingAttr) => throw new NotImplementedException();
            public override MemberInfo[] GetMembers(BindingFlags bindingAttr) => throw new NotImplementedException();
            protected override TypeAttributes GetAttributeFlagsImpl() => throw new NotImplementedException();
            protected override bool IsArrayImpl() => throw new NotImplementedException();
            protected override bool IsByRefImpl() => throw new NotImplementedException();
            protected override bool IsPointerImpl() => throw new NotImplementedException();
            protected override bool IsPrimitiveImpl() => throw new NotImplementedException();
            protected override bool IsCOMObjectImpl() => throw new NotImplementedException();
            public override object InvokeMember(string name, BindingFlags invokeAttr, Binder binder, object target, object[] args, ParameterModifier[] modifiers, CultureInfo culture, string[] namedParameters) => throw new NotImplementedException();
            public override Type UnderlyingSystemType { get; }
            protected override ConstructorInfo GetConstructorImpl(BindingFlags bindingAttr, Binder binder, CallingConventions callConvention, Type[] types, ParameterModifier[] modifiers) => throw new NotImplementedException();
            public override string Name => throw new NotImplementedException();
            public override Guid GUID => throw new NotImplementedException();
            public override Module Module => throw new NotImplementedException();
            public override Assembly Assembly => throw new NotImplementedException();
            public override string Namespace => throw new NotImplementedException();
            public override string AssemblyQualifiedName => throw new NotImplementedException();
            public override Type BaseType => throw new NotImplementedException();
            public override object[] GetCustomAttributes(Type attributeType, bool inherit) => throw new NotImplementedException();

            public override string FullName { get; }

            public override int GetHashCode() => FullName.GetHashCode();

            public override bool Equals(object o) => ReferenceEquals(this, o);
        }

        protected override DbContextOptions ContextOptions { get; }
            = new DbContextOptionsBuilder().UseSqlServer("Server=Dummy").Options;
    }
}
