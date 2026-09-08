/*
    Novedades de la versión 1.10.25.6 (08/09/2026).

    Solo sube la última cifra: son correcciones. Las cuatro salen del repaso de errores de ELMAH de
    hoy, y las tres primeras las estaba sufriendo alguien: fichas de cliente que no se podían abrir,
    fichas que no se podían guardar, y Nesto cerrándose al buscar un producto.

    SE OMITE TODO LO DEMÁS A PROPÓSITO (nada de esto lo nota el usuario de Nesto):
      - Todo el trabajo de las fichas de vídeo de la tienda online (NestoAPI: DescripcionParaFicha,
        FechaBaja, deduplicados, retirada de vídeos): es de la tienda, no de Nesto.
      - La búsqueda de la empresa de un envío pasa a estar en un solo sitio del servicio de agencias
        (Nesto#340): se comporta igual, prepara la migración a la API.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.25.6', '2026-09-08', 'Corregido', 'Ya se pueden abrir las fichas de clientes con más de un contacto principal',
 'Al abrir la ficha de ciertos clientes daba un error y no se podía consultar. Pasaba cuando el cliente tenía más de un contacto marcado como principal, normalmente porque al anular un contacto se le dejó la marca puesta y se creó otro. Ahora se muestra el contacto que está de alta y, si no hay ninguno, el que haya. Afectaba a 79 clientes.', 'Nesto', 1, 'sa'),

('1.10.25.6', '2026-09-08', 'Corregido', 'Los clientes con el NIF repetido vuelven a poder modificarse',
 'Si dos clientes distintos tenían el mismo CIF/NIF, sus fichas se quedaban bloqueadas: al guardar cualquier cambio (una dirección, un teléfono, una cuenta bancaria) saltaba "Ya existe un cliente con ese CIF/NIF" aunque no se hubiera tocado el NIF. Ahora ese aviso solo salta cuando de verdad se cambia el NIF. Afectaba a 26 clientes, que siguen estando duplicados: si hay que unificarlos, es aparte.', 'Nesto', 1, 'sa'),

('1.10.25.6', '2026-09-08', 'Corregido', 'Buscar un producto de canales externos con la casilla vacía ya no cierra Nesto',
 'En la pantalla de productos de canales externos, pulsar Buscar sin escribir nada cerraba la aplicación de golpe. Ahora simplemente no busca nada.', 'Nesto', 1, 'sa'),

('1.10.25.6', '2026-09-08', 'Mejorado', 'Los envíos recanalizados aparecen en Incidentados',
 'Cuando Innovatrans reencamina un envío, hasta ahora se quedaba como "tramitado" y no aparecía en ninguna pestaña, así que podía pasar días parado sin que nadie lo reclamara. Ahora sale en Incidentados con la etiqueta RECANALIZADO.', 'Nesto', 1, 'sa');

SELECT Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.25.6';
