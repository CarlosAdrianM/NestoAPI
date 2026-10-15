/*
    NestoAPI#641 (corte 3 de la épica #639): orden de recorrido por calles.

    Ejecutar en SSMS contra NV como sa (CREATE, ALTER y GRANT: el login nuevavision no puede). Idempotente: se puede
    lanzar dos veces. No está en el EDMX (la API lo lee con SQL a mano, LectorOrdenRecorridoSql), así que se puede lanzar
    antes o después del deploy: SIN ESTE SCRIPT (o sin filas en las tablas) EL ORDEN ES EL DE SIEMPRE (pasillo, columna,
    fila ascendentes).

    Qué crea:
      - RecorridoCalles: una calle = un grupo de pasillos que se recorren a la vez (las dos paredes del mismo pasillo
        físico), con su Orden (saltos 10, 20… para meter una entre dos) y su Sentido ('A' columnas ascendentes, 'D'
        descendentes).
      - RecorridoPasillos: una fila por pasillo (char(3), como en Ubicaciones): su calle (IdCalle, NULL = sin calle), su
        Posicion dentro de la calle (en la h, el 9 antes que el 8) y un Nombre opcional para que alguien nuevo lo
        encuentre (005 = «Esmaltes»; lo pondrá Carlos desde el mantenimiento del corte 4). Un pasillo puede tener nombre
        sin estar en ninguna calle.
      - Almacenes.FilaAMano: desde qué fila se llega con la mano (las filas se numeran de ARRIBA ABAJO; por defecto 4: las
        1, 2 y 3 necesitan escalera). Va en Almacenes junto a los demás parámetros por almacén (FIFO, ControlUbicaciones,
        LineasPickingHora…).

    La regla (OrdenRecorrido.cs): por cada calle, en su orden; dentro, por cada columna (orientada según el sentido):
    primero lo que se coge a mano (fila >= FilaAMano, o la 000) de todos los pasillos de la calle, en su Posicion; luego,
    con la escalera, las filas de arriba de todos esos pasillos; luego la siguiente columna. Los pasillos sin calle (000,
    del 020 en adelante…) van al final, por pasillo, columna y fila, como hoy. Una columna nueva en un pasillo con calle
    hereda su sitio sola.

    Las calles son del almacén físico: si una empresa no tiene las suyas, la API usa las de la empresa 1 (Algete de la
    empresa 3 es el mismo almacén).

    Corte 4 (#642 / Nesto#528): el mantenimiento solo tiene que leer y escribir filas de estas dos tablas (y
    Almacenes.FilaAMano). Para borrar una calle, antes quitar sus pasillos (IdCalle = NULL): la FK no deja calles
    huérfanas ni pasillos de un almacén en la calle de otro.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT, UPDATE y DELETE (el corte 4 edita las calles).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.RecorridoCalles') IS NULL
BEGIN
    CREATE TABLE dbo.RecorridoCalles (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Almacen char(3) NOT NULL,
        Calle varchar(10) NOT NULL,                      -- nombre corto: a, b, c…
        Orden int NOT NULL,                              -- 10, 20, 30…: el orden de las calles en el recorrido
        Sentido char(1) NOT NULL CONSTRAINT DF_RecorridoCalles_Sentido DEFAULT ('A'),
        Descripcion nvarchar(100) NULL,
        Usuario varchar(50) NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_RecorridoCalles_FechaModificacion DEFAULT (GETDATE()),

        CONSTRAINT PK_RecorridoCalles PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RecorridoCalles_Calle UNIQUE (Empresa, Almacen, Calle),
        -- Para la FK compuesta de RecorridoPasillos (un pasillo solo puede ir en una calle de su almacén)
        CONSTRAINT UQ_RecorridoCalles_Id_Almacen UNIQUE (Id, Empresa, Almacen),
        CONSTRAINT CK_RecorridoCalles_Sentido CHECK (Sentido IN ('A', 'D'))
    );
END
GO

IF OBJECT_ID('dbo.RecorridoPasillos') IS NULL
BEGIN
    CREATE TABLE dbo.RecorridoPasillos (
        Empresa char(3) NOT NULL,
        Almacen char(3) NOT NULL,
        Pasillo char(3) NOT NULL,                        -- como en Ubicaciones: '004'
        IdCalle int NULL,                                -- NULL: sin calle (va al final, como hoy)
        Posicion tinyint NULL,                           -- orden del pasillo dentro de su calle (1, 2…)
        Nombre nvarchar(50) NULL,                        -- para que alguien nuevo lo encuentre: 005 = «Esmaltes»
        Usuario varchar(50) NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_RecorridoPasillos_FechaModificacion DEFAULT (GETDATE()),

        CONSTRAINT PK_RecorridoPasillos PRIMARY KEY CLUSTERED (Empresa, Almacen, Pasillo),
        CONSTRAINT FK_RecorridoPasillos_RecorridoCalles FOREIGN KEY (IdCalle, Empresa, Almacen)
            REFERENCES dbo.RecorridoCalles (Id, Empresa, Almacen)
    );
END
GO

-- Por si algún día se crea la tabla sin la columna del nombre (la pidió Carlos el 15/10/26)
IF COL_LENGTH('dbo.RecorridoPasillos', 'Nombre') IS NULL
    ALTER TABLE dbo.RecorridoPasillos ADD Nombre nvarchar(50) NULL;
GO

IF COL_LENGTH('dbo.Almacenes', 'FilaAMano') IS NULL
    ALTER TABLE dbo.Almacenes ADD FilaAMano tinyint NOT NULL
        CONSTRAINT DF_Almacenes_FilaAMano DEFAULT (4)
        CONSTRAINT CK_Almacenes_FilaAMano CHECK (FilaAMano >= 1);
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.RecorridoCalles TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.RecorridoPasillos TO [NUEVAVISION\RDS2016$];
GO

/*
    Semilla: el recorrido de Algete que dio Carlos el 15/10/26. Solo si Algete (empresa 1) aún no tiene ninguna calle:
    relanzar el script no pisa lo que se haya cambiado después desde el mantenimiento.

      a) 1 y 2 a la vez, ascendente          e) 5, ascendente
      b) 11, ascendente                      f) 7 y 13 a la vez, ascendente
      c) 12, ascendente                      g) 10, ascendente
      d) 3 y 4 a la vez, DESCENDENTE         h) 9 y 8 a la vez (primero el 9), DESCENDENTE
      p6) 6, ascendente (después de la h y antes de lo del 20 en adelante)
      resto (000, 020 en adelante…): sin calle, al final como hoy.
*/
IF NOT EXISTS (SELECT 1 FROM dbo.RecorridoCalles WHERE Empresa = '1' AND Almacen = 'ALG')
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.RecorridoCalles (Empresa, Almacen, Calle, Orden, Sentido, Descripcion, Usuario)
    VALUES ('1', 'ALG', 'a', 10, 'A', N'Pasillos 1 y 2 a la vez', 'Issue641'),
           ('1', 'ALG', 'b', 20, 'A', N'Pasillo 11', 'Issue641'),
           ('1', 'ALG', 'c', 30, 'A', N'Pasillo 12', 'Issue641'),
           ('1', 'ALG', 'd', 40, 'D', N'Pasillos 3 y 4 a la vez, de vuelta', 'Issue641'),
           ('1', 'ALG', 'e', 50, 'A', N'Pasillo 5', 'Issue641'),
           ('1', 'ALG', 'f', 60, 'A', N'Pasillos 7 y 13 a la vez', 'Issue641'),
           ('1', 'ALG', 'g', 70, 'A', N'Pasillo 10', 'Issue641'),
           ('1', 'ALG', 'h', 80, 'D', N'Pasillos 9 y 8 a la vez (primero el 9), de vuelta', 'Issue641'),
           ('1', 'ALG', 'p6', 90, 'A', N'Pasillo 6 (después de la h, antes de lo del 20 en adelante)', 'Issue641');

    ;WITH Semilla (Calle, Pasillo, Posicion) AS (
        SELECT 'a', '001', 1 UNION ALL SELECT 'a', '002', 2 UNION ALL
        SELECT 'b', '011', 1 UNION ALL
        SELECT 'c', '012', 1 UNION ALL
        SELECT 'd', '003', 1 UNION ALL SELECT 'd', '004', 2 UNION ALL
        SELECT 'e', '005', 1 UNION ALL
        SELECT 'f', '007', 1 UNION ALL SELECT 'f', '013', 2 UNION ALL
        SELECT 'g', '010', 1 UNION ALL
        SELECT 'h', '009', 1 UNION ALL SELECT 'h', '008', 2 UNION ALL
        SELECT 'p6', '006', 1
    )
    MERGE dbo.RecorridoPasillos AS t
    USING (SELECT c.Empresa, c.Almacen, s.Pasillo, c.Id AS IdCalle, CAST(s.Posicion AS tinyint) AS Posicion
           FROM Semilla s
                INNER JOIN dbo.RecorridoCalles c ON c.Empresa = '1' AND c.Almacen = 'ALG' AND c.Calle = s.Calle) AS o
       ON t.Empresa = o.Empresa AND t.Almacen = o.Almacen AND t.Pasillo = o.Pasillo
    WHEN MATCHED THEN
        UPDATE SET IdCalle = o.IdCalle, Posicion = o.Posicion, Usuario = 'Issue641', FechaModificacion = GETDATE()
    WHEN NOT MATCHED THEN
        INSERT (Empresa, Almacen, Pasillo, IdCalle, Posicion, Usuario)
        VALUES (o.Empresa, o.Almacen, o.Pasillo, o.IdCalle, o.Posicion, 'Issue641');

    COMMIT TRANSACTION;
    PRINT 'Semilla de Algete cargada: 9 calles, 13 pasillos.';
END
ELSE
    PRINT 'Algete ya tenía calles: no se toca la semilla.';
GO

-- Comprobación
SELECT c.Orden, c.Calle, c.Sentido, RTRIM(p.Pasillo) AS Pasillo, p.Posicion, p.Nombre
FROM dbo.RecorridoCalles c
     LEFT JOIN dbo.RecorridoPasillos p ON p.IdCalle = c.Id
WHERE c.Empresa = '1' AND c.Almacen = 'ALG'
ORDER BY c.Orden, p.Posicion;
SELECT RTRIM(Empresa) AS Empresa, RTRIM([Número]) AS Almacen, FilaAMano FROM dbo.Almacenes WHERE [Número] IN ('ALG', 'REI', 'ALC') ORDER BY Empresa, [Número];
GO
