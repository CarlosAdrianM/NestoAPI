-- Script para crear tabla Ganavisiones
-- Issue #94: Sistema Ganavisiones - Backend

CREATE TABLE [dbo].[Ganavisiones] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Empresa] CHAR(3) NOT NULL,
    [ProductoId] CHAR(15) NOT NULL,
    [Ganavisiones] INT NOT NULL,
    [FechaDesde] DATETIME NOT NULL,
    [FechaHasta] DATETIME NULL,
    [FechaCreacion] DATETIME NOT NULL DEFAULT GETDATE(),
    [FechaModificacion] DATETIME NOT NULL DEFAULT GETDATE(),
    [Usuario] NVARCHAR(50) NOT NULL,
    CONSTRAINT [PK_Ganavisiones] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Ganavisiones_Productos] FOREIGN KEY ([Empresa], [ProductoId])
        REFERENCES [dbo].[Productos] ([Empresa], [Número])
);

-- Indice para busquedas por producto
CREATE NONCLUSTERED INDEX [IX_Ganavisiones_ProductoId]
ON [dbo].[Ganavisiones] ([Empresa], [ProductoId]);

-- Indice para busquedas por fechas (productos activos)
CREATE NONCLUSTERED INDEX [IX_Ganavisiones_Fechas]
ON [dbo].[Ganavisiones] ([FechaDesde], [FechaHasta]);
