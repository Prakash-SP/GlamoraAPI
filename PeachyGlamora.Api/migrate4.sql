BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815091315_AddVariantTaggingAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [WishlistItems] ADD [ProductVariantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815091315_AddVariantTaggingAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [ColorSnapshot] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815091315_AddVariantTaggingAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [OrderItems] ADD [SizeSnapshot] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815091315_AddVariantTaggingAndOrderSnapshots'
)
BEGIN
    CREATE INDEX [IX_WishlistItems_ProductVariantId] ON [WishlistItems] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815091315_AddVariantTaggingAndOrderSnapshots'
)
BEGIN
    ALTER TABLE [WishlistItems] ADD CONSTRAINT [FK_WishlistItems_ProductVariants_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariants] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815091315_AddVariantTaggingAndOrderSnapshots'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815091315_AddVariantTaggingAndOrderSnapshots', N'8.0.20');
END;
GO

COMMIT;
GO

