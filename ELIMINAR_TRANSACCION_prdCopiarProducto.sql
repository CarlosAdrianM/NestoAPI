/*******************************************************************************
 * Script: Eliminar transacción interna de prdCopiarProducto
 * Fecha: 2025-01-04
 * Autor: Carlos
 *
 * OBJETIVO:
 * Eliminar el BEGIN TRANSACTION / COMMIT TRANSACTION de prdCopiarProducto
 * para que participe correctamente en la transacción del caller.
 *
 * RAZÓN:
 * Actualmente, prdCopiarProducto tiene un COMMIT interno que hace que
 * los productos se copien inmediatamente y NO se puedan revertir si
 * el traspaso de pedido falla posteriormente.
 *
 * Con este cambio, si el traspaso falla, TODO se revierte (incluyendo productos).
 *
 * PASOS:
 * 1. Hacer backup del stored procedure
 * 2. Modificar el código para eliminar la transacción
 * 3. Probar que funciona correctamente
 * 4. Desplegar en producción
 ******************************************************************************/

-- ===========================================================================
-- PASO 1: BACKUP DEL PROCEDIMIENTO ORIGINAL
-- ===========================================================================
-- Ejecutar este comando para obtener el código actual:
EXEC sp_helptext 'prdCopiarProducto'

-- Guardar el resultado en un archivo .sql como backup antes de modificar


-- ===========================================================================
-- PASO 2: CÓDIGO MODIFICADO (sin BEGIN TRANSACTION / COMMIT)
-- ===========================================================================
-- Este es el nuevo código sin la transacción interna

ALTER PROCEDURE [dbo].[prdCopiarProducto]
    @EmpresaOrigen char(3),
    @EmpresaDestino char(3),
    @NumProducto char(15)
AS
/********************************************************************************
* prdCopiarProducto - Carlos Adrián 30/09/02 - Modificado 04/01/25            *
* ------------------------------------------------------------------            *
*                                                                               *
* Copia un producto de una empresa a otra                                      *
*                                                                               *
* CAMBIO 04/01/25: Eliminada transacción interna para participar en           *
*                  transacciones del caller (ServicioTraspasoEmpresa)          *
********************************************************************************/

-- Declaración de variables
DECLARE @Exito as bit -- Exito = 1 -> Sin errores. Exito = 0 -> Errores
DECLARE @Grupo as char(3) -- grupo del producto
DECLARE @SubGrupo as char(3) -- subgrupo del producto
DECLARE @Existe as char(15) -- guardo el número del producto, grupo o subgrupo en caso de encontrarlo
DECLARE @Familia as char(10) -- familia del producto
DECLARE @Estado as smallint -- para copiar el estado en el caso de que no este
DECLARE @EsEspejo as bit -- para saber si la empresa es espejo
DECLARE @EmpresaEspejo as char(3)

SET @esespejo = 0
SET @empresaespejo = (SELECT [iva por defecto] FROM empresas WHERE numero = @EmpresaOrigen)

-- Miramos si la empresa es espejo
IF @empresaespejo IS NULL OR LTRIM(RTRIM(@empresaespejo)) = '' BEGIN
    SET @esespejo = 1
END ELSE BEGIN
    SET @esespejo = 0
END

-- Inicialización de variables
SET @Exito = 0 -- Con errores

-- Compruebo si existe
SET @Existe = NULL
SELECT @Existe = Número FROM productos WHERE empresa = @EmpresaDestino AND número = @NumProducto

IF @Existe IS NOT NULL BEGIN
    -- El producto existe --> Creo grupo y subgrupo y me salgo
    IF @EsEspejo = 0 BEGIN
        -- Cojo Grupo y SubGrupo de las lineas
        SELECT @Grupo = Grupo, @SubGrupo = SubGrupo, @Familia = Familia, @Estado = Estado
        FROM Productos WHERE empresa = @EmpresaOrigen AND Número = @NumProducto

        -- Creo el Grupo
        SET @Existe = NULL
        SELECT @Existe = Número FROM GruposProducto WHERE empresa = @EmpresaDestino AND número = @Grupo

        IF @Existe IS NULL BEGIN
            INSERT INTO GruposProducto (Empresa, Número, Descripción, ctaventas, ctaabonosvta, ctacompras, ctaabonoscmp, ctaexistencias, ctavariación, ctadepreciación, ctadescuentos)
            SELECT @EmpresaDestino, Número, Descripción, ctaventas, ctaabonosvta, ctacompras, ctaabonoscmp, ctaexistencias, ctavariación, ctadepreciación, ctadescuentos
            FROM GruposProducto WHERE empresa = @EmpresaOrigen AND Número = @Grupo

            IF @@error != 0 BEGIN
                RAISERROR('No se puede crear el grupo en la empresa destino', 11, 1)
                RETURN
            END
        END

        -- Creo el SubGrupo
        SET @Existe = NULL
        SELECT @Existe = Número FROM SubGruposProducto WHERE empresa = @EmpresaDestino AND número = @SubGrupo AND grupo = @Grupo
        IF @existe IS NULL BEGIN
            INSERT INTO SubGruposProducto (Empresa, Grupo, Número, Descripción)
            SELECT @EmpresaDestino, Grupo, Número, Descripción
            FROM subgruposproducto WHERE empresa = @EmpresaOrigen AND Grupo = @Grupo AND número = @SubGrupo

            IF @@error != 0 BEGIN
                RAISERROR('No se puede crear el subgrupo en la empresa destino', 11, 1)
                RETURN
            END
        END

        -- Creo el Estado
        SET @Existe = NULL
        SELECT @Existe = Número FROM estadosproducto WHERE empresa = @EmpresaDestino AND número = @Estado
        IF @existe IS NULL BEGIN
            INSERT INTO EstadosProducto (Empresa, Número, Descripción, AExtinguir, AAnular, SobrePedido, EstadoAlComprar)
            SELECT @EmpresaDestino, Número, Descripción, AExtinguir, AAnular, SobrePedido, EstadoAlComprar
            FROM estadosproducto WHERE empresa = @EmpresaOrigen AND número = @Estado

            IF @@error != 0 BEGIN
                RAISERROR('No se puede crear el estado en la empresa destino', 11, 1)
                RETURN
            END
        END
    END ELSE BEGIN
        SELECT @Familia = Familia FROM productos WHERE Empresa = @EmpresaOrigen AND Número = @NumProducto
    END

    -- Creo la Familia
    SET @Existe = NULL
    SELECT @Existe = Número FROM Familias WHERE empresa = @EmpresaDestino AND número = @Familia

    IF @existe IS NULL BEGIN
        INSERT INTO Familias (Empresa, Número, Descripción)
        SELECT @EmpresaDestino, Número, Descripción
        FROM Familias WHERE empresa = @EmpresaOrigen AND número = @Familia

        IF @@error != 0 BEGIN
            RAISERROR('No se puede crear la familia en la empresa destino', 11, 1)
            RETURN
        END
    END

    RETURN -- Porque el producto ya existía
END ELSE
    SET @Exito = 1

-- ⚠️ CAMBIO: Eliminado BEGIN TRANSACTION aquí

-- Creo el grupo y el subgrupo
IF @Exito = 1 BEGIN
    IF @esespejo = 0 BEGIN
        SELECT @Grupo = Grupo, @SubGrupo = SubGrupo, @Familia = Familia, @Estado = estado
        FROM productos WHERE Empresa = @EmpresaOrigen AND Número = @NumProducto

        IF (SELECT número FROM gruposproducto WHERE empresa = @EmpresaDestino AND número = @Grupo) IS NULL BEGIN
            INSERT INTO GruposProducto (Empresa, Número, Descripción, ctaventas, ctaabonosvta, ctacompras, ctaabonoscmp, ctaexistencias, ctavariación, ctadepreciación, ctadescuentos)
            SELECT @EmpresaDestino, Número, Descripción, ctaventas, ctaabonosvta, ctacompras, ctaabonoscmp, ctaexistencias, ctavariación, ctadepreciación, ctadescuentos
            FROM GruposProducto WHERE empresa = @EmpresaOrigen AND Número = @Grupo

            IF @@error != 0 BEGIN
                RAISERROR('No se puede copiar el grupo %d a la empresa destino', 11, 1, @Grupo)
                SET @Exito = 0
            END
        END

        IF (SELECT número FROM subgruposproducto WHERE empresa = @EmpresaDestino AND grupo = @Grupo AND número = @SubGrupo) IS NULL BEGIN
            INSERT INTO SubGruposProducto (Empresa, Grupo, Número, Descripción)
            SELECT @EmpresaDestino, Grupo, Número, Descripción
            FROM subgruposproducto WHERE empresa = @EmpresaOrigen AND Grupo = @Grupo AND número = @SubGrupo

            IF @@error != 0 BEGIN
                RAISERROR('No se puede copiar el subgrupo %d a la empresa destino', 11, 1, @Subgrupo)
                SET @Exito = 0
            END
        END

        IF (SELECT Número FROM estadosproducto WHERE empresa = @EmpresaDestino AND número = @Estado) IS NULL BEGIN
            INSERT INTO EstadosProducto (Empresa, Número, Descripción, AExtinguir, AAnular, SobrePedido, EstadoAlComprar)
            SELECT @EmpresaDestino, Número, Descripción, AExtinguir, AAnular, SobrePedido, EstadoAlComprar
            FROM estadosproducto WHERE empresa = @EmpresaOrigen AND número = @Estado

            IF @@error != 0 BEGIN
                RAISERROR('No se puede crear el estado en la empresa destino', 11, 1)
                RETURN
            END
        END

    END ELSE BEGIN
        SELECT @Familia = Familia, @Estado = estado FROM productos WHERE Empresa = @EmpresaOrigen AND Número = @NumProducto

        IF (SELECT Número FROM estadosproducto WHERE empresa = @EmpresaDestino AND número = @Estado) IS NULL BEGIN
            INSERT INTO EstadosProducto (Empresa, Número, Descripción, AExtinguir, AAnular, SobrePedido, EstadoAlComprar)
            SELECT @EmpresaDestino, Número, Descripción, AExtinguir, AAnular, SobrePedido, EstadoAlComprar
            FROM estadosproducto WHERE empresa = @EmpresaOrigen AND número = @Estado

            IF @@error != 0 BEGIN
                RAISERROR('No se puede crear el estado en la empresa destino', 11, 1)
                RETURN
            END
        END
    END

    IF (SELECT número FROM Familias WHERE empresa = @EmpresaDestino AND número = @Familia) IS NULL BEGIN
        INSERT INTO Familias (Empresa, Número, Descripción)
        SELECT @EmpresaDestino, Número, Descripción
        FROM Familias WHERE empresa = @EmpresaOrigen AND Número = @Familia

        IF @@error != 0 BEGIN
            RAISERROR('No se puede copiar la familia %d a la empresa destino', 11, 1, @Familia)
            SET @Exito = 0
        END
    END
END

-- Copio ficha del producto
IF @Exito = 1 BEGIN
    IF @esespejo = 0 BEGIN
        INSERT INTO Productos (Empresa, Número, Nombre, Grupo, PVP, [IVA Soportado], [IVA repercutido], comentarios, estado, [codbarras], [aplicar dto], subgrupo, tamaño, unidadmedida, familia)
        SELECT @EmpresaDestino, Número, Nombre, Grupo, PVP, [IVA Soportado], [IVA repercutido], comentarios, estado, [codbarras], [aplicar dto], subgrupo, tamaño, unidadmedida, @Familia
        FROM Productos WHERE Empresa = @EmpresaOrigen AND Número = @NumProducto

        IF @@error != 0 BEGIN
            RAISERROR('No se pueden crear los productos en la empresa destino', 1, 1)
            SET @Exito = 0
        END
    END ELSE BEGIN
        INSERT INTO Productos (Empresa, Número, Nombre, PVP, [IVA Soportado], [IVA repercutido], comentarios, estado, [codbarras], [aplicar dto], tamaño, unidadmedida, familia)
        SELECT @EmpresaDestino, Número, Nombre, PVP, [IVA Soportado], [IVA repercutido], comentarios, estado, [codbarras], [aplicar dto], tamaño, unidadmedida, @Familia
        FROM Productos WHERE Empresa = @EmpresaOrigen AND Número = @NumProducto

        IF @@error != 0 BEGIN
            RAISERROR('No se pueden crear los productos en la empresa destino', 1, 1)
            SET @Exito = 0
        END
    END

    -- Copio los descuentos del producto
    IF @EsEspejo = 0 BEGIN
        INSERT INTO DescuentosProducto(Empresa, [Nº Producto], GrupoProducto, [nº cliente], contacto, [NºProveedor], ContactoProveedor, CantidadMínima, Descuento, Precio, Familia)
        SELECT @EmpresaDestino, c1.[Nº Producto], c1.GrupoProducto, c1.[nº cliente], c1.contacto, c1.[NºProveedor], c1.ContactoProveedor, c1.CantidadMínima, c1.Descuento, c1.Precio, c1.Familia
        FROM DescuentosProducto AS c1
        WHERE c1.[Nº producto] = @NumProducto
          AND c1.empresa = @EmpresaOrigen
          AND (c1.[nº cliente] IN (SELECT [nº cliente] FROM clientes WHERE empresa = @EmpresaDestino) OR c1.[nº cliente] IS NULL)
          AND (c1.[nºproveedor] IS NULL OR c1.[nºproveedor] IN (SELECT número FROM proveedores WHERE empresa = @EmpresaDestino))

        IF @@error != 0 BEGIN
            RAISERROR('No se pueden crear los descuentos productos en la empresa destino', 1, 1)
            SET @Exito = 0
        END
    END
END

-- ⚠️ CAMBIO: Eliminado COMMIT TRANSACTION / ROLLBACK aquí
-- Ahora el caller (ServicioTraspasoEmpresa) controla la transacción

-- Si hubo errores, lanzar excepción para que el caller haga rollback
IF @Exito = 0 BEGIN
    RAISERROR('Error al copiar el producto', 16, 1)
    RETURN -1
END

RETURN 0
GO


-- ===========================================================================
-- PASO 3: PROBAR EN DESARROLLO
-- ===========================================================================
/*
-- Test básico: Copiar un producto
BEGIN TRANSACTION

EXEC prdCopiarProducto '1', '3', 'TEST001'

-- Verificar que se copió
SELECT * FROM Productos WHERE Empresa = '3' AND Número = 'TEST001'

-- Hacer rollback para verificar que se revierte correctamente
ROLLBACK TRANSACTION

-- Verificar que el producto NO quedó en empresa 3
SELECT * FROM Productos WHERE Empresa = '3' AND Número = 'TEST001'
-- Debe retornar 0 filas
*/


-- ===========================================================================
-- PASO 4: DESPLEGAR EN PRODUCCIÓN
-- ===========================================================================
/*
IMPORTANTE:
1. Hacer backup de la base de datos antes
2. Ejecutar el ALTER PROCEDURE en horario de bajo tráfico
3. Probar inmediatamente con un traspaso de prueba
4. Monitorear logs por si hay errores
5. Tener listo el rollback (código original) por si hay problemas

ROLLBACK: Si hay problemas, ejecutar:
EXEC sp_helptext 'prdCopiarProducto' -- para obtener el código de backup
-- Y luego ejecutar el ALTER PROCEDURE con el código original (con BEGIN TRANSACTION)
*/


-- ===========================================================================
-- NOTAS FINALES
-- ===========================================================================
/*
VENTAJAS DE ESTE CAMBIO:
✅ Los productos se revierten si el traspaso de pedido falla
✅ Participación correcta en la transacción del caller
✅ Mayor consistencia transaccional
✅ Menos efectos colaterales

DESVENTAJAS:
⚠️ Si varios procesos llaman a prdCopiarProducto simultáneamente sin transacción,
   podrían haber errores de duplicate key (poco probable en este caso)

IMPACTO:
- Bajo: Este procedimiento solo se llama desde ServicioTraspasoEmpresa
- El cambio mejora la consistencia sin introducir riesgos
*/
