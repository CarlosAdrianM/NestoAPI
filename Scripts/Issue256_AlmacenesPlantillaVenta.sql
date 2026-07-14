-- Issue #256: parámetro de usuario para elegir de qué almacenes se muestra el stock en la
-- plantilla de venta (Nesto y NestoApp). La API (PonerStock) lee este parámetro como fuente
-- de verdad; el default '(defecto)' = los tres almacenes deja el comportamiento EXACTAMENTE
-- igual que hoy hasta que cada usuario cambie su preferencia desde la UI.
--
-- El mecanismo de herencia ya existente copia la fila de (defecto) al usuario concreto la
-- primera vez que la lee (ParametrosUsuarioController.LeerParametro).
--
-- ⚠️ ORDEN SEGURO: ejecutar ANTES de desplegar la API (sin la fila, la API usa el fallback
-- del cliente: también inocuo). BD: NV (NestoConnection). Sin GRANTs (INSERT de datos).

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'AlmacenesPlantillaVenta' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'AlmacenesPlantillaVenta', '(defecto)', 'ALG,ALC,REI', 'NestoAPI', GETDATE());
END
GO

-- VERIFICACIÓN (debe devolver 1 fila con Valor = 'ALG,ALC,REI'):
SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario
WHERE Clave = 'AlmacenesPlantillaVenta' AND Usuario = '(defecto)';
GO
