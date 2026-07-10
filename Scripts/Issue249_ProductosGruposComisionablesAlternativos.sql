-- Issue #249: grupo de producto variable según el vendedor que comisiona (productos marcados).
-- Tabla many-to-many: cada fila marca un producto como "convertible" al GrupoAlternativo indicado.
-- Un producto puede tener 1..n grupos alternativos (además del de su ficha, que es implícito).
--
-- Regla (simétrica, decidida el 10/07/26): al crear una línea (o cambiar su producto), si quien
-- mete el pedido comisiona por alguno de los grupos candidatos, la línea se convierte a ese grupo,
-- SALVO que el grupo de ficha esté protegido por un vendedor real en VendedoresClienteGrupoProducto
-- (solo se puede "pisar" un grupo sin registro, con vendedor NULL o con vendedor NV). Si el pedido
-- no lo mete ninguno de los vendedores del cliente, se queda el grupo de la ficha.
--
-- ⚠️ EJECUTAR EN PRODUCCIÓN ANTES DE DESPLEGAR LA API (el EDMX ya mapea la tabla).

CREATE TABLE ProductosGruposComisionablesAlternativos (
    Id int IDENTITY(1,1) NOT NULL,
    Empresa char(3) NOT NULL,
    Producto char(15) NOT NULL,
    GrupoAlternativo char(3) NOT NULL,
    Usuario nvarchar(30) NOT NULL CONSTRAINT DF_ProductosGruposComisionablesAlternativos_Usuario DEFAULT SUSER_SNAME(),
    FechaModificacion datetime NOT NULL CONSTRAINT DF_ProductosGruposComisionablesAlternativos_FechaModificacion DEFAULT GETDATE(),
    CONSTRAINT PK_ProductosGruposComisionablesAlternativos PRIMARY KEY (Id),
    CONSTRAINT UQ_ProductosGruposComisionablesAlternativos UNIQUE (Empresa, Producto, GrupoAlternativo),
    CONSTRAINT FK_ProductosGruposComisionablesAlternativos_Productos
        FOREIGN KEY (Empresa, Producto) REFERENCES Productos (Empresa, Número),
    CONSTRAINT FK_ProductosGruposComisionablesAlternativos_GruposProducto
        FOREIGN KEY (Empresa, GrupoAlternativo) REFERENCES GruposProducto (Empresa, Número)
);
GO

-- La API entra con la cuenta de máquina (ver feedback de GRANTs de la BD NV).
GRANT SELECT, INSERT, UPDATE, DELETE ON ProductosGruposComisionablesAlternativos TO [NUEVAVISION\RDS2016$];
GO

-- Alta de ejemplo (guantes de nitrilo convertibles a PEL) — ajustar referencias reales al usarla:
-- INSERT INTO ProductosGruposComisionablesAlternativos (Empresa, Producto, GrupoAlternativo)
-- VALUES ('1  ', '12345          ', 'PEL');
