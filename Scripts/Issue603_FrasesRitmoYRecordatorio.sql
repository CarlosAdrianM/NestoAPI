/*
    NestoAPI#603 (cortes 4 y 5): frases de ritmo y recordatorio semanal de las sugerencias de contacto.

    Ejecutar en SSMS contra NV como sa (crea tablas: el login nuevavision no tiene ALTER). Idempotente: se puede lanzar dos
    veces. Mejor ANTES de publicar la API; si se publica antes, no rompe nada: la frase sale de las plantillas sin memoria
    (y queda en ELMAH que falta FrasesRitmo) y el recordatorio no encuentra la lista de vendedores, así que no avisa a nadie.
    No están en el EDMX: la API las lee y escribe con SqlClient.

    - FrasesRitmo (corte 4): la frase elegida para el panel de ritmo de cada vendedor, una por día y situación (Arranque,
      PorDebajo, ObjetivoCumplido…). Texto lleva las variables sin sustituir ({hoy}, {faltan:contacto}…), para que los
      números se pongan al día en cada consulta. Plantilla = clave del banco (a1, d3…) u «OpenAI». Las 10 últimas de cada
      vendedor son la memoria para no repetir.
    - SugerenciasContactoRecordatorios (corte 5): cada aviso del job «recordatorio-sugerencias-contacto» (como mucho uno por
      semana y vendedor).
    - ParametrosUsuario «(defecto)» SugerenciasContactoAvisarA: los vendedores a los que se recuerda la lista. Vacío o «0»
      = a nadie. Se lee en cada ejecución: ampliar la lista no necesita publicar.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.FrasesRitmo') IS NULL
BEGIN
    CREATE TABLE dbo.FrasesRitmo (
        Id int IDENTITY(1, 1) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_FrasesRitmo_Fecha DEFAULT (GETDATE()),
        Vendedor char(3) NOT NULL,
        Situacion varchar(30) NOT NULL,
        Plantilla varchar(20) NOT NULL,
        Texto nvarchar(400) NOT NULL,
        CONSTRAINT PK_FrasesRitmo PRIMARY KEY (Id)
    );
    CREATE INDEX IX_FrasesRitmo_Vendedor_Fecha ON dbo.FrasesRitmo (Vendedor, Fecha);
END
GO

GRANT SELECT, INSERT ON dbo.FrasesRitmo TO [NUEVAVISION\RDS2016$];
GO

IF OBJECT_ID('dbo.SugerenciasContactoRecordatorios') IS NULL
BEGIN
    CREATE TABLE dbo.SugerenciasContactoRecordatorios (
        Id int IDENTITY(1, 1) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_SugerenciasContactoRecordatorios_Fecha DEFAULT (GETDATE()),
        Vendedor char(3) NOT NULL,
        Usuarios varchar(200) NOT NULL,     -- usuarios (sin dominio) a cuya campana llegó, separados por comas
        Texto nvarchar(400) NOT NULL,
        CONSTRAINT PK_SugerenciasContactoRecordatorios PRIMARY KEY (Id)
    );
    CREATE INDEX IX_SugerenciasContactoRecordatorios_Vendedor_Fecha ON dbo.SugerenciasContactoRecordatorios (Vendedor, Fecha);
END
GO

GRANT SELECT, INSERT ON dbo.SugerenciasContactoRecordatorios TO [NUEVAVISION\RDS2016$];
GO

-- Decisión de Carlos (07/10/26): de momento, solo los seis.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'SugerenciasContactoAvisarA' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'SugerenciasContactoAvisarA', '(defecto)', 'MPP,LHY,PA,JGP,IM,DLS', 'NestoAPI', GETDATE());
END
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: las dos tablas (vacías la primera vez), el parámetro y a quién llegaría el aviso
------------------------------------------------------------------------------------------------
SELECT 'FrasesRitmo' AS Tabla, COUNT(*) AS Filas FROM dbo.FrasesRitmo
UNION ALL SELECT 'SugerenciasContactoRecordatorios', COUNT(*) FROM dbo.SugerenciasContactoRecordatorios;

SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'SugerenciasContactoAvisarA' AND Usuario = '(defecto)';

SELECT RTRIM(Valor) AS Vendedor, RTRIM(Usuario) AS Usuario FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'Vendedor' AND RTRIM(Valor) IN ('MPP', 'LHY', 'PA', 'JGP', 'IM', 'DLS') AND Usuario <> '(defecto)'
ORDER BY 1, 2;
GO
