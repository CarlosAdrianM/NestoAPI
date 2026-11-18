/*
    NestoAPI#857e (18/11/26, aprobado por Carlos): AVISOS DE SALIDA DEL PROVEEDOR en los correos de seguimiento de compras.
    Caso real: el 17/11/26 sales@dduueett.com (proveedor 1021, no-reply) mandó «Adjuntamos albarán de entrega» con un packing list en PDF
    («REF 220923», «Compañía de transporte: SCHENKER STANDARD») y el job no lo guardó (ni código de seguimiento ni remitente marcado).

    No se separan correos de agencia y de proveedor: se amplían los remitentes marcados.
    Cambia dbo.AgenciasEntradaRemitentes (la de Scripts/Issue857_ComprasCorreosSeguimiento.sql):
      - Agencia pasa a NULL: un remitente SIN agencia es el de un proveedor que avisa de la salida (el proveedor sale como siempre:
        contacto > dominio > nombre).
      - FiltroTexto nvarchar(500) NULL: solo cuentan sus correos que lo dicen en el asunto o en el cuerpo (sin distinguir mayúsculas ni
        tildes; varias alternativas separadas por «|»). NULL = todos sus correos (como hasta ahora).
      - LeerPdf bit NOT NULL DEFAULT 0: 1 = se descargan EN MEMORIA sus PDF adjuntos (3 como mucho, 5 MB y 10 páginas) y se saca el
        pedido, la agencia y la entrega. Del PDF no se guarda nada: solo lo extraído (abajo).
      - Ignorar bit NOT NULL DEFAULT 0: 1 = remitente que no manda nada de envíos (boletines): sus correos ni se guardan, ni se
        reenvían, ni avisan, traigan lo que traigan. Se marca por el POST de remitentes o al descartar un correo con
        ?ignorarRemitente=true (caso 18/11/26: el boletín de comunicaciones@perfectbeauty.es captado por un enlace de clics).
    Añade a dbo.ComprasCorreosSeguimiento:
      - AvisoProveedor bit NULL: 1 = viene de un remitente marcado sin agencia. Si además no trae código ni enlace de seguimiento, su
        fecha aplicada NO cuenta como «seguimiento de la agencia» (la certeza queda Aproximada, no Prácticamente segura).
      - PedidoEnAdjunto int NULL: el pedido de compra que se ha leído del PDF (en el aire y del proveedor).
      - AdjuntoLeido nvarchar(20) NULL: SinPdf, Leido, SinTexto (escaneado), Ilegible, Grande o NoDescargado; NULL = no se pidió leerlo.

    ORDEN: después de Issue857 (y de 857b/c/d, que ya están en producción). Puede ir ANTES o DESPUÉS de publicar la API: sin TODO lo de
    este script, la API sigue como antes (los remitentes, con agencia obligatoria; sin filtro ni PDF) y lo vuelve a comprobar cada 10
    minutos, sin reiniciar. Tras lanzarlo, dar de alta el remitente por el endpoint (ejemplo al final) y, si se quiere repasar lo de los
    últimos días, POST api/Compras/AgenciasEntrada/Correos/Reanalizar?dias=3 (que sí lee los PDF; no aplica fechas).

    Lanzar como sa en NV. Idempotente: se puede relanzar. Cada columna nueva en su propio lote. Permisos: las dos tablas ya los tienen
    (GRANT SELECT, INSERT, UPDATE a [NUEVAVISION\RDS2016$] en Issue857): no hace falta ninguno nuevo.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NULL OR OBJECT_ID(N'dbo.AgenciasEntradaRemitentes', N'U') IS NULL
    RAISERROR(N'No existen las tablas de Scripts/Issue857_ComprasCorreosSeguimiento.sql: lance antes ese script. No se ha cambiado nada.', 16, 1);
GO
-- Remitentes: la agencia pasa a opcional (remitente de un proveedor)
IF OBJECT_ID(N'dbo.AgenciasEntradaRemitentes', N'U') IS NOT NULL
   AND COLUMNPROPERTY(OBJECT_ID(N'dbo.AgenciasEntradaRemitentes'), N'Agencia', N'AllowsNull') = 0
    ALTER TABLE dbo.AgenciasEntradaRemitentes ALTER COLUMN Agencia nvarchar(50) NULL;
GO
IF OBJECT_ID(N'dbo.AgenciasEntradaRemitentes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AgenciasEntradaRemitentes', N'FiltroTexto') IS NULL
    ALTER TABLE dbo.AgenciasEntradaRemitentes ADD FiltroTexto nvarchar(500) NULL;
GO
IF OBJECT_ID(N'dbo.AgenciasEntradaRemitentes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AgenciasEntradaRemitentes', N'LeerPdf') IS NULL
    ALTER TABLE dbo.AgenciasEntradaRemitentes ADD LeerPdf bit NOT NULL CONSTRAINT DF_AgenciasEntradaRemitentes_LeerPdf DEFAULT (0);
GO
IF OBJECT_ID(N'dbo.AgenciasEntradaRemitentes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AgenciasEntradaRemitentes', N'Ignorar') IS NULL
    ALTER TABLE dbo.AgenciasEntradaRemitentes ADD Ignorar bit NOT NULL CONSTRAINT DF_AgenciasEntradaRemitentes_Ignorar DEFAULT (0);
GO
-- Correos: lo extraído del aviso del proveedor
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'AvisoProveedor') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD AvisoProveedor bit NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'PedidoEnAdjunto') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD PedidoEnAdjunto int NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'AdjuntoLeido') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD AdjuntoLeido nvarchar(20) NULL;
GO
-- Comprobación (la misma que hace la API)
SELECT CASE WHEN COLUMNPROPERTY(OBJECT_ID(N'dbo.AgenciasEntradaRemitentes'), N'Agencia', N'AllowsNull') = 1
             AND COL_LENGTH(N'dbo.AgenciasEntradaRemitentes', N'FiltroTexto') IS NOT NULL
             AND COL_LENGTH(N'dbo.AgenciasEntradaRemitentes', N'LeerPdf') IS NOT NULL
             AND COL_LENGTH(N'dbo.AgenciasEntradaRemitentes', N'Ignorar') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'AvisoProveedor') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'PedidoEnAdjunto') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'AdjuntoLeido') IS NOT NULL
            THEN N'OK: remitentes con Agencia opcional, FiltroTexto, LeerPdf e Ignorar; correos con AvisoProveedor, PedidoEnAdjunto y AdjuntoLeido'
            ELSE N'FALTA: no está todo (¿existen las tablas de Issue857?)' END AS Resultado;
GO
/*
    EJEMPLO (NO se lanza aquí): el alta real la hace Carlos o Compras por el endpoint, con su usuario (queda en Usuario):

    POST api/Compras/AgenciasEntrada/Remitentes
    { "Agencia": null, "Remitente": "sales@dduueett.com", "FiltroTexto": "albarán de entrega|delivery note", "LeerPdf": true }

    Y el boletín que no es de envíos (o, mejor, al descartar su correo: POST …/Correos/{id}/Descartar?ignorarRemitente=true):
    POST api/Compras/AgenciasEntrada/Remitentes
    { "Agencia": null, "Remitente": "comunicaciones@perfectbeauty.es", "Ignorar": true }

    Equivalen a (solo como referencia):
    INSERT INTO dbo.AgenciasEntradaRemitentes (Agencia, Remitente, Activo, Usuario, FechaModificacion, FiltroTexto, LeerPdf, Ignorar)
    VALUES (NULL, N'sales@dduueett.com', 1, N'Carlos', GETDATE(), N'albarán de entrega|delivery note', 1, 0);
    INSERT INTO dbo.AgenciasEntradaRemitentes (Agencia, Remitente, Activo, Usuario, FechaModificacion, FiltroTexto, LeerPdf, Ignorar)
    VALUES (NULL, N'comunicaciones@perfectbeauty.es', 1, N'Carlos', GETDATE(), NULL, 0, 1);
*/
