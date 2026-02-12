using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Project.Domain.Entities;
using Project.Domain.ValueObjects;
using Project.Domain.Enums;

namespace Project.Infrastructure.Persistence.Configuration;

public static class MongoDbMappings
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;

        BsonSerializer.RegisterSerializer(new EnumSerializer<UserRole>(MongoDB.Bson.BsonType.String));

        BsonClassMap.RegisterClassMap<User>(cm =>
        {
            cm.AutoMap();
            cm.SetIdMember(cm.GetMemberMap(u => u.Id));
            cm.MapProperty(u => u.Email).SetSerializer(new EmailSerializer());
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<EmailMessage>(cm =>
        {
            cm.AutoMap();
            cm.SetIdMember(cm.GetMemberMap(e => e.Id));
            cm.SetIgnoreExtraElements(true);
        });

        _registered = true;
    }
}

public class EmailSerializer : IBsonSerializer<Email>
{
    public Type ValueType => typeof(Email);

    public Email Deserialize(MongoDB.Bson.Serialization.BsonDeserializationContext context, MongoDB.Bson.Serialization.BsonDeserializationArgs args)
    {
        var value = context.Reader.ReadString();
        return Email.Create(value);
    }

    public void Serialize(MongoDB.Bson.Serialization.BsonSerializationContext context, MongoDB.Bson.Serialization.BsonSerializationArgs args, Email value)
    {
        context.Writer.WriteString(value.Value);
    }

    object IBsonSerializer.Deserialize(MongoDB.Bson.Serialization.BsonDeserializationContext context, MongoDB.Bson.Serialization.BsonDeserializationArgs args)
        => Deserialize(context, args);

    public void Serialize(MongoDB.Bson.Serialization.BsonSerializationContext context, MongoDB.Bson.Serialization.BsonSerializationArgs args, object value)
        => Serialize(context, args, (Email)value);
}
