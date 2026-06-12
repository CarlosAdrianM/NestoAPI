-- Issue Nesto#372: Changelog de novedades para el usuario al actualizar
-- Tabla satélite (NO está en el EDMX): la API la lee con SqlQuery en ServicioNovedades.
-- Las entradas se insertan a mano (o con ayuda de IA, con revisión humana) al publicar versión.
-- REGLA DE ORO: lenguaje de usuario, nunca técnico. Solo cambios que el usuario percibe.

CREATE TABLE dbo.Novedades (
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Novedades PRIMARY KEY,
    [Version] VARCHAR(23) NOT NULL,             -- versión ClickOnce de Nesto a la que se asocia
    Fecha DATE NOT NULL CONSTRAINT DF_Novedades_Fecha DEFAULT (GETDATE()),
    Categoria NVARCHAR(20) NOT NULL,            -- Nuevo / Mejorado / Corregido
    Titulo NVARCHAR(200) NOT NULL,
    Descripcion NVARCHAR(1000) NULL,
    Ambito NVARCHAR(20) NOT NULL CONSTRAINT DF_Novedades_Ambito DEFAULT ('Nesto'), -- Nesto / NestoAPI
    Publicada BIT NOT NULL CONSTRAINT DF_Novedades_Publicada DEFAULT (1),
    Usuario NVARCHAR(50) NULL,
    Fecha_Modificación DATETIME NOT NULL CONSTRAINT DF_Novedades_FechaModificacion DEFAULT (GETDATE()),
    CONSTRAINT CK_Novedades_Categoria CHECK (Categoria IN ('Nuevo', 'Mejorado', 'Corregido'))
);
GO

-- La API corre con la cuenta de máquina del servidor RDS (ver convención de GRANTs del proyecto)
GRANT SELECT ON dbo.Novedades TO [NUEVAVISION\RDS2016$];
GO

-- =====================================================================================
-- SEMILLA: novedades de la próxima versión. AJUSTAR [Version] a la versión ClickOnce
-- que se vaya a publicar antes de ejecutar este bloque.
-- =====================================================================================
/*
DECLARE @version VARCHAR(23) = '1.10.6.0'; -- <-- AJUSTAR

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
(@version, 'Corregido', 'Aviso claro cuando un cliente tiene descuentos duplicados',
 'Si un cliente tiene condiciones de descuento duplicadas, al buscar un producto ahora se indica exactamente qué familia o producto hay que corregir, en vez de dar un error genérico.', 'NestoAPI', SUSER_SNAME()),
(@version, 'Corregido', 'Nesto ya no se cierra al teclear un producto en un pedido nuevo',
 'En pedidos recién creados, teclear un producto antes de elegir cliente podía cerrar Nesto y dejar la línea con el IVA sin calcular.', 'Nesto', SUSER_SNAME()),
(@version, 'Mejorado', 'Mensajes de error más claros al buscar productos',
 'Cuando hay un problema al consultar un producto, se muestra el motivo real en vez de "El producto no existe".', 'Nesto', SUSER_SNAME()),
(@version, 'Nuevo', 'Ventana de novedades',
 'Al actualizar Nesto se muestra un resumen de las mejoras incluidas. También se puede consultar en Herramientas → Ayuda → Novedades.', 'Nesto', SUSER_SNAME());
*/
GO

-- Consulta de comprobación
-- SELECT * FROM dbo.Novedades ORDER BY Id DESC;
