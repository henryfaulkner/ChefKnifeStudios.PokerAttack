namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.BlobStorage;

public record GenerateSasTokenResDTO(
    string SasUrl,
    DateTimeOffset ExpiresOn
);
