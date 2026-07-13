-- Issue #287: mover el contador de asientos (UltAsientoContable) de la fila de Empresas a una
-- tabla satélite POR EMPRESA, para que la contabilización no bloquee a los lectores de Empresas.
--
-- CONTEXTO (capturado en vivo el 13/07/26, ver #271): prdContabilizar abre transacción y lo
-- PRIMERO que hace es actualizar Empresas.UltAsientoContable, reteniendo el lock X sobre la
-- fila de Empresas (la configuración más leída del sistema) durante TODA la contabilización.
-- Cualquier lector de Empresas (crear pedido incluido) se queda detrás.
--
-- ANÁLISIS PREVIO (13/07/26, con VIEW DEFINITION):
--   - Único escritor: prdContabilizar (2 puntos: al inicio y dentro del bucle por cada asiento).
--   - prdEmpresasUsuario solo LEE la columna (es víctima del bloqueo, no causa).
--   - 18 SPs llaman a prdContabilizar (prdCrearFacturaVta, prdCrearFacturaCmp, prdLiquidar...):
--     cambiando prdContabilizar quedan cubiertos todos.
--   - En C# (NestoAPI) y VB (Nesto) la columna solo está mapeada en el modelo; nadie la lee.
--
-- La toma del número sigue DENTRO de la transacción: si hay rollback, el contador se revierte
-- con todo lo demás (CERO huecos en la numeración de asientos, requisito de Carlos).
--
-- ⚠️ EJECUTAR FUERA DE HORARIO (sin contabilizaciones en curso), y aplicar la PARTE 2 (edición
-- de prdContabilizar en SSMS) inmediatamente después, en la misma ventana de mantenimiento:
-- entre la parte 1 y la parte 2 el SP sigue usando la columna vieja.

------------------------------------------------------------------------------------------------
-- PARTE 1: tabla satélite de contadores por empresa (ejecutable tal cual)
------------------------------------------------------------------------------------------------

CREATE TABLE ContadoresEmpresa (
    Empresa char(3) NOT NULL CONSTRAINT PK_ContadoresEmpresa PRIMARY KEY,
    UltAsientoContable int NOT NULL,
    CONSTRAINT FK_ContadoresEmpresa_Empresas FOREIGN KEY (Empresa) REFERENCES Empresas(Número)
);
GO

-- Semilla con el valor vigente de cada empresa (por eso hay que ejecutarlo sin contabilizaciones
-- en curso: el valor copiado debe ser el último real).
INSERT INTO ContadoresEmpresa (Empresa, UltAsientoContable)
    SELECT Número, UltAsientoContable FROM Empresas;
GO

-- BD de negocio (NestoConnection): GRANT a la cuenta de máquina del API. Hoy solo el SP toca la
-- tabla (ownership chaining bastaría), pero así queda lista si algún día EF la lee directamente.
GRANT SELECT, INSERT, UPDATE ON ContadoresEmpresa TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- PARTE 2: edición de prdContabilizar (aplicar A MANO en SSMS sobre el fuente real del SP)
------------------------------------------------------------------------------------------------
-- No se incluye aquí el ALTER PROCEDURE completo a propósito: el SP tiene ~33 KB con
-- identificadores acentuados y es más seguro editar los DOS bloques localizados que regenerar
-- el cuerpo entero desde un volcado.
--
-- prdContabilizar toma el contador en DOS sitios con el mismo patrón leer-luego-actualizar
-- (no atómico) sobre Empresas. Sustituir AMBOS por un único UPDATE atómico sobre la tabla nueva.
--
-- ── BLOQUE 1 (al comienzo de la transacción, tras 'begin transaction') ──────────────────────
-- ANTES:
--     declare @UltAsiento int
--     set @UltAsiento=(select UltAsientoContable from empresas where número=@empresa)
--     if @@error!= 0 begin
--         raiserror('No se ha podido encontrar el ultimo asiento de la contabilidad',16,1)
--         rollback
--         close crsPreContab
--         deallocate crsPreContab
--         return(-2)
--     end
--     update empresas set UltAsientoContable=@ultasiento+1 where número=@empresa
--     if @@error!= 0 begin
--         raiserror('No se ha podido actualizar el último asiento en la tabla empresas',16,1)
--         rollback
--         close crsPreContab
--         deallocate crsPreContab
--         return(-2)
--     end
--
-- DESPUÉS:
--     declare @UltAsiento int
--     update ContadoresEmpresa
--        set @UltAsiento = UltAsientoContable,
--            UltAsientoContable = UltAsientoContable + 1
--      where Empresa = @empresa
--     if @@error != 0 or @@rowcount = 0 begin
--         raiserror('No se ha podido actualizar el último asiento en ContadoresEmpresa',16,1)
--         rollback
--         close crsPreContab
--         deallocate crsPreContab
--         return(-2)
--     end
--
-- ── BLOQUE 2 (dentro del bucle del cursor, en el 'if @AsientoAnterior != @asiento') ─────────
-- ANTES (mismo patrón select+update sobre empresas, sin el declare):
--     set @UltAsiento=(select UltAsientoContable from empresas where número=@empresa)
--     ... (if @@error / rollback) ...
--     update empresas set UltAsientoContable=@ultasiento+1 where número=@empresa
--     ... (if @@error / rollback) ...
--
-- DESPUÉS (idéntico al bloque 1 pero SIN el 'declare'):
--     update ContadoresEmpresa
--        set @UltAsiento = UltAsientoContable,
--            UltAsientoContable = UltAsientoContable + 1
--      where Empresa = @empresa
--     if @@error != 0 or @@rowcount = 0 begin
--         raiserror('No se ha podido actualizar el último asiento en ContadoresEmpresa',16,1)
--         rollback
--         close crsPreContab
--         deallocate crsPreContab
--         return(-2)
--     end
--
-- ⚠️ SEMÁNTICA CLAVE (por qué el resto del SP no se toca): en un UPDATE de T-SQL todas las
-- expresiones de la derecha usan los valores PRE-update, así que '@UltAsiento = UltAsientoContable'
-- deja en @UltAsiento el valor ANTIGUO (igual que el SELECT actual). Los INSERT del SP siguen
-- usando @UltAsiento+1 y el 'return @UltAsiento + 1' final no cambia. Además el UPDATE único
-- elimina la carrera leer-luego-escribir del patrón actual.
--
-- Tras el cambio, Empresas.UltAsientoContable queda CONGELADO (valor histórico del día de la
-- migración). No borrar la columna todavía: está mapeada en el EDMX de NestoAPI y en
-- Nesto.Models; eliminarla requiere regenerar modelos (hacerlo en una fase posterior).

------------------------------------------------------------------------------------------------
-- VUELTA ATRÁS (por si algo falla tras el cambio)
------------------------------------------------------------------------------------------------
-- ANTES de editar prdContabilizar: guardar su fuente actual (clic derecho en SSMS →
-- 'Incluir procedimiento almacenado como' → ALTER) en un .sql aparte.
--
-- Para revertir: (1) resincronizar la columna vieja con el valor vivo del contador nuevo:
--     UPDATE e SET e.UltAsientoContable = c.UltAsientoContable
--     FROM Empresas e INNER JOIN ContadoresEmpresa c ON c.Empresa = e.Número;
-- (2) restaurar el prdContabilizar original guardado. La tabla ContadoresEmpresa puede quedarse
-- (inofensiva); no hay que tocar nada más.

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN (tras aplicar parte 1 + parte 2 y contabilizar un diario de prueba)
------------------------------------------------------------------------------------------------
-- 1) El contador nuevo avanza y el viejo queda congelado:
--    SELECT e.Número, e.UltAsientoContable AS Viejo_Congelado, c.UltAsientoContable AS Nuevo
--    FROM Empresas e LEFT JOIN ContadoresEmpresa c ON c.Empresa = e.Número;
-- 2) El número de asiento devuelto es correlativo con el último de Contabilidad:
--    SELECT MAX(Asiento) FROM Contabilidad WHERE Empresa = '1';
-- 3) Durante una facturación larga, sp_lock ya NO debe mostrar X sobre RIDs de Empresas
--    (ObjId de Empresas = OBJECT_ID('Empresas')); solo sobre ContadoresEmpresa.
