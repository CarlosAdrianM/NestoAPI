/*
    Novedades de la versión 1.10.51.1 de Nesto (28/10/2026, versión menor sobre la 1.10.51.0 de esta tarde).
    Versión de la ClickOnce: 1.10.51.1 (pubxml 1.10.51.* : al publicar sube sola la revisión a 1).

    Tanda: sugerencia 854 (picking de primera hora «Solo nuestra ruta», decisión de Carlos 28/10): NestoAPI
    GET api/Picking/RutasPropias + Nesto radio nuevo en el popup del picking.

    ORDEN:
      1) Publicar NestoAPI (master) y la ClickOnce (Clean + Rebuild) → 1.10.51.1.
      2) DESPUÉS: ESTE script y, con el Id que devuelve, subir el vídeo (24 s):
         POST api/Novedades/{id}/Video con scratchpad/hyperframes/picking/picking-primera-hora.mp4 (único vídeo de la versión).
      3) Contestar en la sugerencia 854 a @Alfredo @Santiago @Manuel que ya está publicado y marcarla Implementada en 1.10.51.1
         (PUT api/Novedades/Sugerencias/854).

    SE OMITE A PROPÓSITO: nada más va en esta versión.

    Ejecutar en SSMS contra NV como sa.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.51.1';
DECLARE @fecha DATE = '2026-10-28';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, 'Nuevo', N'Picking de primera hora: solo nuestra ruta (AT y 16)',
    N'En «Sacar Picking» hay una opción nueva, «Solo nuestra ruta (AT y 16)»: saca únicamente los pedidos con ruta AT o 16 y entrega hasta hoy. Al llegar por la mañana, antes de sacarlo, salen como siempre los pedidos de OnTime que seguramente irían en la furgoneta, para pasarlos a AT o 16 con un clic. Con ese picking se prepara la ruta propia y los repartidores salen a las 9. Todo lo demás (agencias) lo saca el picking automático de las 11, y con él se prepara la ruta de agencias completa, sin ampliar nada a mano. Lo que entre después va al siguiente picking. Vea el vídeo de 24 segundos. Lo pidieron Alfredo y Santiago.',
    'Nesto', 1, 'sa', N'Almacén,Dirección'
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = N'Picking de primera hora: solo nuestra ruta (AT y 16)');

SELECT Id, Titulo FROM Novedades WHERE Version = @version; -- el Id para subir el vídeo
