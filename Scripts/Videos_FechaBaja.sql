/*
    Videos.FechaBaja - marcar la baja de un vídeo en vez de borrar la fila.

    PARA QUÉ
    --------
    Hoy la tabla Videos no tiene NINGUNA columna de estado, así que el listado /api/Videos no puede
    decir que un vídeo está de baja: simplemente deja de devolverlo. La tienda online deduce la baja
    por ausencia (recorre el listado entero y da de baja lo que no aparece) y para que un fallo de la
    API no despublique media web tiene que rodearlo de guardas: solo reconcilia si el recorrido llegó
    al final de forma inequívoca, y nunca si los ausentes pasan del 10 % del catálogo. Cada guarda es
    un sitio donde algo puede salir mal.

    Y hay un segundo motivo, más serio: retirar un vídeo HOY es un DELETE
    (ConsolaNVIA, GestorTranscripciones.DeleteVideoSiExiste) con ON DELETE CASCADE a VideosProductos.
    Se lleva por delante la transcripción, el protocolo y los productos asociados, y eso no se
    recupera. Con FechaBaja la retirada deja de destruir nada.

    POR QUÉ FECHA Y NO UN BOOLEANO "Activo"
    ---------------------------------------
    Cuesta lo mismo y responde además a "¿cuándo se retiró?", que es justo lo que hoy no se puede
    contestar de los 77 vídeos que la tienda tuvo que dar de baja el 07/09/26. NULL = vivo.

    CÓMO SE EJECUTA
    ---------------
    Es DDL, así que hay que lanzarlo desde SSMS con `sa` (el login `nuevavision` no tiene ALTER).
    No hace falta ningún GRANT nuevo: los permisos son de tabla y la tabla ya los tiene.

    LO QUE HABÍA QUE TOCAR DESPUÉS: YA ESTÁ HECHO (08/09/26, mismo día)
    -------------------------------------------------------------------
    Esta lista se escribió ANTES de cablearlo. Se deja como registro de lo que llevó el cambio, pero
    NO queda nada pendiente: el script y el código van juntos.

    1. ✅ EDMX (NestoEntities.edmx) editado A MANO en los tres modelos: SSDL (Property FechaBaja
       datetime), CSDL (Property FechaBaja DateTime nulable) y MSL (ScalarProperty FechaBaja ->
       columna FechaBaja), más la propiedad en Models/Video.cs. Hay precedente de editarlo a mano
       (commits 20af92f y 415a99a).
       ⚠️ NO hace falta "Update Model from Database" desde Visual Studio, y conviene NO hacerlo: es
       lo que en #413 dejó una propiedad fantasma. Los tests VideoFechaBajaTests vigilan que el
       mapeo siga estando en los tres modelos, así que si alguien refresca el EDMX y se lo lleva,
       la suite lo caza en vez de que se descubra en producción.
    2. ✅ ServicioVideos: filtro `v.FechaBaja == null` en las cuatro consultas y FechaBaja en
       VideoLookupModel; VideosController.GetVideos acepta `bool incluirBajas = false`.
    3. ✅ VideosController.GetVideoById devuelve NotFound si FechaBaja no es null.
    4. ✅ ConsolaNVIA: DeleteVideoSiExiste -> DarDeBajaVideoSiExiste (UPDATE), y ADEMÁS
       ReactivarVideoSiEstabaDeBaja, que no estaba en esta lista y hacía falta: con el DELETE, un
       vídeo que volvía a ser público se reinsertaba solo; con la baja, la fila sigue ahí y hay que
       quitarle la FechaBaja o se quedaría retirado para siempre.
    5. ✅ Buscador Lucene: ObtenerVideos filtra WHERE FechaBaja IS NULL.
    6. ✅ (tampoco estaba en la lista) ServicioRecomendacionesPostCompra: sus tres consultas de
       vídeos filtran las bajas, para que un correo post-compra no recomiende un vídeo retirado.
*/

USE NV;
GO

SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Videos') AND name = 'FechaBaja')
BEGIN
    PRINT 'La columna Videos.FechaBaja ya existe: no se hace nada.';
END
ELSE
BEGIN
    ALTER TABLE dbo.Videos ADD FechaBaja datetime NULL;
    PRINT 'Columna Videos.FechaBaja creada (NULL = el vídeo está vivo).';
END
GO

/*
    Índice del listado. El listado ordena por FechaPublicacion y, con el filtro nuevo, solo mira
    los vivos: un índice filtrado lo cubre entero y no crece con las bajas.
*/
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Videos') AND name = 'IX_Videos_Vivos_FechaPublicacion')
BEGIN
    CREATE NONCLUSTERED INDEX IX_Videos_Vivos_FechaPublicacion
        ON dbo.Videos (FechaPublicacion DESC, Id)
        WHERE FechaBaja IS NULL;
    PRINT 'Índice IX_Videos_Vivos_FechaPublicacion creado.';
END
GO

-- Comprobación
SELECT 'Vivos'   AS Estado, COUNT(*) AS Videos FROM dbo.Videos WHERE FechaBaja IS NULL
UNION ALL
SELECT 'De baja' AS Estado, COUNT(*)           FROM dbo.Videos WHERE FechaBaja IS NOT NULL;
GO
