-- NestoAPI#827 (sugerencia 959, Sancho; Carlos 05/11/26): una sola grafía por persona.
-- Ejecutar como sa en NV. Mejor DESPUÉS de publicar la API con #827 (si no, Nesto vuelve a grabar «NUEVAVISION\Dlopez»).
--
-- Windows devuelve el usuario con la caja con la que se tecleó al iniciar sesión y Nesto lo mandaba tal cual: los rapports de
-- Daniel salían con «NUEVAVISION\dlopez» y «NUEVAVISION\Dlopez» y el informe de rapports lo partía en dos. Desde #827 la API graba
-- la grafía canónica (UsuarioAuditoriaHelper.Persona): la del usuario de Identity («DLopez», «MariaJose») o inicial en mayúscula y
-- el resto en minúscula («Aida», «Santiago», «Reina»). Este script deja lo ya grabado con esa misma grafía.
--
-- EL MAPA VA EXPLÍCITO ABAJO (#Mapa): revísalo antes de lanzar. Salió de los distintos (con COLLATE Latin1_General_CS_AS) de las
-- cuatro tablas, solo del dominio NUEVAVISION, el 05/11/26. Compuestos con grafía propia: DLopez y MariaJose (los dos en Identity,
-- así salen en ELMAH [User]) y CGarcia (solo en EnviosAgencia, antigua; ya estaba también así). No existe «LauraC»: Laura Camacho
-- es «Reina».
--
-- Filas que cambiaría (recuento del 05/11/26, todas las fechas): ver el bloque «Recuentos 05/11/26» al final. El script los
-- vuelve a sacar antes y después. Idempotente: ninguna canónica es una grafía de origen, así que la segunda vez no toca nada.
-- SeguimientoCliente, PedidosModoServicioOrigen, EnviosAgencia e Inventarios no tienen triggers (comprobado 05/11/26).
--
-- NO SE TOCAN (a propósito):
--   - LogModificaciones y demás tablas de auditoría que rellenan los triggers: son histórico, se dejan como se grabaron.
--   - CabPedidoVta, LinPedidoVta, ExtractoCliente, ExtractoProducto, Contabilidad, CabFacturaVta…: llevan triggers y mucho volumen;
--     SQL compara sin caja, así que no afecta a nada salvo a lo que se agrupa en memoria (y eso ya lo hace la API sin caja).
--   - FechasPermitidas y ParámetrosUsuario: la API los busca sin caja (#823); no hace falta.
--   - NotificacionesBuzon y DispositivosNotificaciones: las lee la API con NombresDeLaPersona (sin caja en SQL).
--   - Usuarios de otro dominio (DOMINIO\…, Inventarios antiguos), cuentas de máquina (NUEVAVISION\RDS2016$, SERVER1$), los raros
--     con dos barras (NUEVAVISION\Patricia\Laura, NUEVAVISION\reina\Laura) y «NUEVAVISION\Iñaki» (no es la caja: lleva eñe; si se
--     quiere unir con «Inaki», es otra decisión).
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('tempdb..#Mapa') IS NOT NULL DROP TABLE #Mapa;
CREATE TABLE #Mapa (
    Actual   varchar(50) COLLATE Latin1_General_CS_AS NOT NULL PRIMARY KEY,
    Canonica varchar(50) COLLATE Latin1_General_CS_AS NOT NULL
);

INSERT INTO #Mapa (Actual, Canonica) VALUES
    -- con más de una grafía en SeguimientoCliente desde 2025
    ('NUEVAVISION\aida',          'NUEVAVISION\Aida'),
    ('NUEVAVISION\ALMUDENA',      'NUEVAVISION\Almudena'),
    ('NUEVAVISION\CARLOS',        'NUEVAVISION\Carlos'),
    ('NUEVAVISION\dlopez',        'NUEVAVISION\DLopez'),      -- Identity: DLopez
    ('NUEVAVISION\Dlopez',        'NUEVAVISION\DLopez'),
    ('NUEVAVISION\inaki',         'NUEVAVISION\Inaki'),
    ('NUEVAVISION\israel',        'NUEVAVISION\Israel'),
    ('NUEVAVISION\jesus',         'NUEVAVISION\Jesus'),
    ('NUEVAVISION\laura',         'NUEVAVISION\Laura'),
    ('NUEVAVISION\mamen',         'NUEVAVISION\Mamen'),
    ('NUEVAVISION\manuel',        'NUEVAVISION\Manuel'),
    ('NUEVAVISION\Mariajose',     'NUEVAVISION\MariaJose'),   -- Identity: MariaJose
    ('NUEVAVISION\marta',         'NUEVAVISION\Marta'),
    ('NUEVAVISION\MARTA',         'NUEVAVISION\Marta'),
    ('NUEVAVISION\monica',        'NUEVAVISION\Monica'),
    ('NUEVAVISION\paloma',        'NUEVAVISION\Paloma'),
    ('NUEVAVISION\pilar',         'NUEVAVISION\Pilar'),
    -- en minúsculas o mayúsculas en las otras tablas (aunque en SeguimientoCliente ya estén bien)
    ('NUEVAVISION\PALOMA',        'NUEVAVISION\Paloma'),
    ('NUEVAVISION\reina',         'NUEVAVISION\Reina'),       -- Laura Camacho (no hay «LauraC»)
    ('NUEVAVISION\REINA',         'NUEVAVISION\Reina'),
    ('NUEVAVISION\santiago',      'NUEVAVISION\Santiago'),
    ('NUEVAVISION\SANTIAGO',      'NUEVAVISION\Santiago'),
    ('NUEVAVISION\alfredo',       'NUEVAVISION\Alfredo'),
    ('NUEVAVISION\pedro',         'NUEVAVISION\Pedro'),
    ('NUEVAVISION\noelia',        'NUEVAVISION\Noelia'),
    ('NUEVAVISION\administrador', 'NUEVAVISION\Administrador'),
    ('NUEVAVISION\cgarcia',       'NUEVAVISION\CGarcia'),     -- compuesto antiguo; ya había «CGarcia»
    -- históricos (solo antes de 2025 en SeguimientoCliente): para que tampoco se partan los informes de años pasados.
    -- Compuestos con la grafía mayoritaria de entonces: CGarcia, LAlonso, MariaJose, RosaMaria. Si no los quieres tocar, bórralos.
    ('NUEVAVISION\CGARCIA',       'NUEVAVISION\CGarcia'),
    ('NUEVAVISION\lalonso',       'NUEVAVISION\LAlonso'),
    ('NUEVAVISION\Lalonso',       'NUEVAVISION\LAlonso'),
    ('NUEVAVISION\mariajose',     'NUEVAVISION\MariaJose'),
    ('NUEVAVISION\rosamaria',     'NUEVAVISION\RosaMaria'),
    ('NUEVAVISION\alba',          'NUEVAVISION\Alba'),
    ('NUEVAVISION\anaisdelhoyo',  'NUEVAVISION\Anaisdelhoyo'),
    ('NUEVAVISION\angel',         'NUEVAVISION\Angel'),
    ('NUEVAVISION\antonio',       'NUEVAVISION\Antonio'),
    ('NUEVAVISION\carolina',      'NUEVAVISION\Carolina'),
    ('NUEVAVISION\eloisa',        'NUEVAVISION\Eloisa'),
    ('NUEVAVISION\gandullo',      'NUEVAVISION\Gandullo'),
    ('NUEVAVISION\gema',          'NUEVAVISION\Gema'),
    ('NUEVAVISION\javier',        'NUEVAVISION\Javier'),
    ('NUEVAVISION\juanma',        'NUEVAVISION\Juanma'),
    ('NUEVAVISION\maria',         'NUEVAVISION\Maria'),
    ('NUEVAVISION\nereida',       'NUEVAVISION\Nereida'),
    ('NUEVAVISION\nuria',         'NUEVAVISION\Nuria'),
    ('NUEVAVISION\sandra',        'NUEVAVISION\Sandra'),
    ('NUEVAVISION\silvia',        'NUEVAVISION\Silvia'),
    ('NUEVAVISION\yolanda',       'NUEVAVISION\Yolanda'),
    ('NUEVAVISION\ZAMORA',        'NUEVAVISION\Zamora');

-- Cordura del mapa: ninguna canónica puede ser a su vez una grafía de origen (idempotencia) y solo cambia la caja.
IF EXISTS (SELECT 1 FROM #Mapa m JOIN #Mapa o ON o.Actual = m.Canonica)
   OR EXISTS (SELECT 1 FROM #Mapa WHERE UPPER(Actual) <> UPPER(Canonica) OR Actual = Canonica)
BEGIN
    RAISERROR('El mapa no es válido: revísalo (una canónica es origen, o cambia algo más que la caja).', 16, 1);
    RETURN;
END;

-- Recuento ANTES
SELECT 'ANTES' AS Momento, t.Tabla, t.Filas FROM (
              SELECT 'SeguimientoCliente' AS Tabla, COUNT(*) AS Filas FROM dbo.SeguimientoCliente s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual WHERE s.Usuario LIKE 'NUEVAVISION\%'
    UNION ALL SELECT 'PedidosModoServicioOrigen', COUNT(*) FROM dbo.PedidosModoServicioOrigen s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual WHERE s.Usuario LIKE 'NUEVAVISION\%'
    UNION ALL SELECT 'EnviosAgencia', COUNT(*) FROM dbo.EnviosAgencia s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual WHERE s.Usuario LIKE 'NUEVAVISION\%'
    UNION ALL SELECT 'Inventarios', COUNT(*) FROM dbo.Inventarios s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual WHERE s.Usuario LIKE 'NUEVAVISION\%'
) t;

BEGIN TRAN;

UPDATE s SET Usuario = m.Canonica
FROM dbo.SeguimientoCliente s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual
WHERE s.Usuario LIKE 'NUEVAVISION\%';
PRINT CONCAT('SeguimientoCliente: ', @@ROWCOUNT, ' filas');

UPDATE s SET Usuario = m.Canonica
FROM dbo.PedidosModoServicioOrigen s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual
WHERE s.Usuario LIKE 'NUEVAVISION\%';
PRINT CONCAT('PedidosModoServicioOrigen: ', @@ROWCOUNT, ' filas');

UPDATE s SET Usuario = m.Canonica
FROM dbo.EnviosAgencia s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual
WHERE s.Usuario LIKE 'NUEVAVISION\%';
PRINT CONCAT('EnviosAgencia: ', @@ROWCOUNT, ' filas');

UPDATE s SET Usuario = m.Canonica
FROM dbo.Inventarios s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual
WHERE s.Usuario LIKE 'NUEVAVISION\%';
PRINT CONCAT('Inventarios: ', @@ROWCOUNT, ' filas');

-- Recuento DESPUÉS (todo a 0)
DECLARE @Quedan int =
      (SELECT COUNT(*) FROM dbo.SeguimientoCliente s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual WHERE s.Usuario LIKE 'NUEVAVISION\%')
    + (SELECT COUNT(*) FROM dbo.PedidosModoServicioOrigen s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual WHERE s.Usuario LIKE 'NUEVAVISION\%')
    + (SELECT COUNT(*) FROM dbo.EnviosAgencia s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual WHERE s.Usuario LIKE 'NUEVAVISION\%')
    + (SELECT COUNT(*) FROM dbo.Inventarios s JOIN #Mapa m ON RTRIM(s.Usuario) COLLATE Latin1_General_CS_AS = m.Actual WHERE s.Usuario LIKE 'NUEVAVISION\%');
PRINT CONCAT('DESPUÉS, filas con una grafía del mapa: ', @Quedan, ' (se espera 0)');

IF @Quedan = 0
BEGIN
    COMMIT;
    PRINT 'COMMIT';
END
ELSE
BEGIN
    ROLLBACK;
    PRINT 'ROLLBACK: algo no ha cuadrado, no se ha cambiado nada';
END;

-- Lo que siga con más de una grafía en SeguimientoCliente (desde 2025): debería salir vacío. Si sale alguien, añadirlo al mapa.
SELECT UPPER(RTRIM(Usuario)) AS Persona, COUNT(DISTINCT RTRIM(Usuario) COLLATE Latin1_General_CS_AS) AS Grafias
FROM dbo.SeguimientoCliente
WHERE Fecha >= '20250101' AND Usuario LIKE 'NUEVAVISION\%'
GROUP BY UPPER(RTRIM(Usuario))
HAVING COUNT(DISTINCT RTRIM(Usuario) COLLATE Latin1_General_CS_AS) > 1;

DROP TABLE #Mapa;
GO

-- Recuentos 05/11/26 (todas las fechas, filas que cambiaría; sacados con el login de solo lectura):
--   SeguimientoCliente         142.910 (88.364 con el mapa de 2025 y 54.546 con los históricos)
--   PedidosModoServicioOrigen       70 (dlopez 11, Dlopez 2, jesus 34, MARTA 22, CARLOS 1)
--   EnviosAgencia                  368 (aida 101, cgarcia 221, monica 46)
--   Inventarios                 10.760 (administrador 605, aida 4.399, alfredo 402, noelia 239, paloma 582, PALOMA 237,
--                                       pedro 165, pilar 9, reina 2.991, santiago 1.119, SANTIAGO 12)
--   Los que más pesan en SeguimientoCliente: jesus 24.667, Mariajose 20.848, paloma 11.280, cgarcia 7.522, lalonso/Lalonso,
--   aida 4.738, MARTA 4.793, dlopez 4.456. Sin triggers, pero Usuario está en ~20 índices DTA de
--   SeguimientoCliente y ~11 de EnviosAgencia (ninguno único): el UPDATE de SeguimientoCliente bloquea un rato. FUERA DE HORARIO.
