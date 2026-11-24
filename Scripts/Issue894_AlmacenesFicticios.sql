/*
    NestoAPI#894 (Carlos 24/11/26): almacenes ficticios de los vendedores: consulta y traspasos contra el almacén propio
    (GET api/AlmacenesFicticios, GET api/AlmacenesFicticios/{almacen}/Existencias, POST api/AlmacenesFicticios/Traspasos).

    Lo que hace (para sa, idempotente; se puede lanzar antes o después de publicar, pero ANTES de que nadie traspase):
      1. ParametrosUsuario empresa '1', clave 'AlmacenFicticio', fila «(defecto)» VACÍA: sin valor propio, el usuario no tiene almacén
         ficticio (la API nunca crea la fila del usuario al leer, pero Nesto viejo con LeerParametro sí la copiaría de «(defecto)»).
      2. El diario de sistema de los traspasos, «_Ficticio» (DiariosProducto empresa '1', Sistema = 1, sin recálculo de precio medio):
         PreExtrProducto.Diario tiene clave ajena a DiariosProducto, así que sin él el primer traspaso falla.
      3. Plantilla COMENTADA para dar a cada vendedor su almacén (NO se lanza: la rellena Carlos con los de verdad).

    Sin GRANT nuevo: la API ya lee Almacenes, ParametrosUsuario, Familias, Productos, ExtractoProducto y Ubicaciones y ya escribe en
    PreExtrProducto, Ubicaciones, ContadoresGlobales (NºTraspaso) y Modificaciones (y llama a prdExtrProducto y sp_getapplock).
    Vuelta atrás: borrar las dos filas de los pasos 1 y 2 (y las de los vendedores) cuando no haya nada en PreExtrProducto con ese diario.
*/

SET NOCOUNT ON;

-- 1. La fila «(defecto)» del parámetro, vacía
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'AlmacenFicticio' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'AlmacenFicticio', '(defecto)', '', 'Issue894', GETDATE());
GO

-- 2. El diario de sistema de los traspasos con los almacenes ficticios
IF NOT EXISTS (SELECT 1 FROM DiariosProducto WHERE Empresa = '1' AND [Número] = '_Ficticio')
    INSERT INTO DiariosProducto (Empresa, [Número], [Descripción], Sistema, RecalculaPrecioMedio, Usuario, [Fecha Modificación])
    VALUES ('1', '_Ficticio', 'Traspasos con almacenes ficticios (API)', 1, 0, 'Issue894', GETDATE());
GO

-- 3. PLANTILLA (comentada a propósito): el almacén ficticio de cada vendedor. Usuario = el de Windows SIN dominio, como en el resto
--    de ParametrosUsuario; Valor = el código del almacén ficticio. El jefe de equipo ve los de su equipo por su parámetro «Vendedor»:
--    conviene que cada dueño tenga también su fila «Vendedor» (la de NestoApp se reconoce por el claim «Vendedor» del token).
--
-- DECLARE @usuario varchar(30) = 'Israel', @almacen char(3) = 'IF';
-- IF EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'AlmacenFicticio' AND Usuario = @usuario)
--     UPDATE ParametrosUsuario SET Valor = @almacen, Usuario2 = 'Issue894', [Fecha Modificación] = GETDATE()
--     WHERE Empresa = '1' AND Clave = 'AlmacenFicticio' AND Usuario = @usuario;
-- ELSE
--     INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
--     VALUES ('1', 'AlmacenFicticio', @usuario, @almacen, 'Issue894', GETDATE());
-- GO

-- Comprobaciones (solo lectura)
SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS AlmacenFicticio FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'AlmacenFicticio' ORDER BY CASE WHEN Usuario = '(defecto)' THEN 0 ELSE 1 END, Usuario;

SELECT RTRIM([Número]) AS Diario, RTRIM([Descripción]) AS Descripcion, Sistema, RecalculaPrecioMedio FROM DiariosProducto
WHERE Empresa = '1' AND [Número] IN ('_Ficticio', '_ErrRepo');

-- Los almacenes ficticios activos que todavía no son de nadie (tendrán dueño cuando se rellene el paso 3)
SELECT RTRIM(a.[Número]) AS Almacen, RTRIM(a.[Descripción]) AS Descripcion
FROM Almacenes a
WHERE a.Empresa = '1' AND a.AlmacénFicticio = 1 AND ISNULL(a.Estado, 0) >= 0
  AND NOT EXISTS (SELECT 1 FROM ParametrosUsuario p WHERE p.Empresa = '1' AND p.Clave = 'AlmacenFicticio' AND p.Usuario <> '(defecto)'
                  AND UPPER(LTRIM(RTRIM(p.Valor))) = RTRIM(a.[Número]))
ORDER BY a.[Número];
GO
