/*
    NestoAPI#577 (corte 1): calendario de reposiciones tienda → Algete y hora de corte del picking.

    Ejecutar en SSMS contra NV como sa (crea una tabla: el login nuevavision no tiene ALTER). Idempotente: se puede
    lanzar dos veces. Se puede lanzar ANTES o DESPUÉS de publicar la API: sin la tabla, GET api/Reposiciones/ProximaLlegada
    falla (la tabla está en el EDMX), así que conviene lanzarlo ANTES del deploy.

    Datos iniciales (decisión de Carlos, 07/10/26):
      - Reina → Algete: lunes, miércoles y viernes.
      - Alcobendas → Algete: lunes, martes y jueves.
      - HoraCierre 10:00 (a esa hora se rellena sola la reposición y la tienda la prepara) y HoraLlegadaHabitual 13:30.
      - Corte del picking en Algete: 11:00 (parámetro HoraCortePicking, fila «(defecto)»; sin la fila la API usa 11:00).

    El calendario lo mantiene Almacén (PUT api/Reposiciones/Calendario). DiaSemana: 1 lunes … 7 domingo.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. Tabla
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.ReposicionesCalendario') IS NULL
BEGIN
    CREATE TABLE dbo.ReposicionesCalendario (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        AlmacenOrigen char(3) NOT NULL,                 -- la tienda que manda la reposición (REI, ALC)
        AlmacenDestino char(3) NOT NULL,                -- quien la recibe (ALG)
        DiaSemana tinyint NOT NULL,                     -- 1 lunes … 7 domingo
        HoraCierre time(0) NOT NULL,                    -- a esa hora se rellena sola y la tienda la prepara
        HoraLlegadaHabitual time(0) NOT NULL,           -- a qué hora suele entrar en el destino (el mismo día)
        Activo bit NOT NULL CONSTRAINT DF_ReposicionesCalendario_Activo DEFAULT (1),
        Usuario varchar(30) NOT NULL CONSTRAINT DF_ReposicionesCalendario_Usuario DEFAULT (SUSER_SNAME()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ReposicionesCalendario_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_ReposicionesCalendario PRIMARY KEY (Id),
        CONSTRAINT UQ_ReposicionesCalendario_Ruta UNIQUE (Empresa, AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre),
        CONSTRAINT CK_ReposicionesCalendario_DiaSemana CHECK (DiaSemana BETWEEN 1 AND 7),
        CONSTRAINT CK_ReposicionesCalendario_Almacenes CHECK (AlmacenOrigen <> AlmacenDestino),
        CONSTRAINT CK_ReposicionesCalendario_Horas CHECK (HoraLlegadaHabitual >= HoraCierre)
    );
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ReposicionesCalendario TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- 2. Calendario de hoy (solo si la ruta aún no tiene filas)
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.ReposicionesCalendario WHERE Empresa = '1' AND AlmacenOrigen = 'REI' AND AlmacenDestino = 'ALG')
BEGIN
    INSERT INTO dbo.ReposicionesCalendario (Empresa, AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, Activo, Usuario, FechaModificacion)
    VALUES
        ('1', 'REI', 'ALG', 1, '10:00', '13:30', 1, 'NestoAPI#577', GETDATE()),
        ('1', 'REI', 'ALG', 3, '10:00', '13:30', 1, 'NestoAPI#577', GETDATE()),
        ('1', 'REI', 'ALG', 5, '10:00', '13:30', 1, 'NestoAPI#577', GETDATE());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ReposicionesCalendario WHERE Empresa = '1' AND AlmacenOrigen = 'ALC' AND AlmacenDestino = 'ALG')
BEGIN
    INSERT INTO dbo.ReposicionesCalendario (Empresa, AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, Activo, Usuario, FechaModificacion)
    VALUES
        ('1', 'ALC', 'ALG', 1, '10:00', '13:30', 1, 'NestoAPI#577', GETDATE()),
        ('1', 'ALC', 'ALG', 2, '10:00', '13:30', 1, 'NestoAPI#577', GETDATE()),
        ('1', 'ALC', 'ALG', 4, '10:00', '13:30', 1, 'NestoAPI#577', GETDATE());
END
GO

------------------------------------------------------------------------------------------------
-- 3. Hora de corte del picking (Algete): 11:00. Fila «(defecto)»: sin ella la API usa 11:00 igualmente.
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'HoraCortePicking' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'HoraCortePicking', '(defecto)', '11:00', 'NestoAPI', GETDATE());
END
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: 6 filas (REI 1/3/5, ALC 1/2/4, 10:00 → 13:30) y el parámetro a '11:00'
------------------------------------------------------------------------------------------------
SELECT Id, Empresa, AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, Activo, Usuario
FROM dbo.ReposicionesCalendario
ORDER BY AlmacenOrigen, DiaSemana, HoraCierre;

SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario
WHERE Clave = 'HoraCortePicking' AND Usuario = '(defecto)';
GO
