/*
    OneShot 08/09/26 (c) - Retirar del catálogo los vídeos en los que aparece Noelia.

    DE DÓNDE SALE LA LISTA
    ----------------------
    Decisión de dirección trasladada por el equipo de SEO el 08/09/26. Ellos no podían hacer la
    lista: buscando en las 771 fichas de la tienda salían solo 4, porque la tienda no guarda la
    transcripción y varios de estos vídeos no la nombran ni en el título ni en la descripción.

    Buscando en Nesto sobre título + descripción + protocolo + TRANSCRIPCIÓN salen 19. Pero de esos
    19, ocho son espectadoras llamadas Noelia que preguntan en un directo y a las que Yolanda lee en
    voz alta (hay al menos tres Noelias distintas: Fernández, Fernández Martínez, Martín). Esas NO
    se retiran: mención no es aparición.

    Quedan estos 11, con la frase que lo demuestra:

        1314  "se lo voy a hacer hoy a mi compañera Noelia, que tiene decoloración en el cabello"
        1393  "yo soy Noelia y yo soy Elena, y somos las profesoras de Alcobendas"
        1432  "Voy a aplicar en Noelia una pequeña cantidad para que veáis la diferencia"
        1475  "Uy Noelia, pero qué haces ahí, que te he pillado pensando"
        1489  "Noelia nos cuenta en qué consiste el curso"
        1516  "hoy estoy con Noelia... es una de mis alumnas de estética"
        1538  "Noelia se maquilla los labios para mostrarnos las bondades"
        1718  "en el caso de Noelia voy a trabajar un ojo para que veáis la diferencia"
        1719  "la piel de Noelia es un fototipo 2, es una piel fina"
        1787  "Si es Noelia quien nos da los buenos días con la nueva Multiterapia"
        1798  "lo vamos a probar en Noelia"

    OJO CON EL 1050: estaba en la lista de cuatro que mandó SEO y es un FALSO POSITIVO. Su
    descripción dice "Pilar se estrena en la Feria Online hablándonos de... A Noelia la habéis visto
    en el vídeo anterior". Quien presenta es Pilar. No se retira.

    OJO CON EL 1516: la transcripción dice "hoy estoy con Noelia, NO LE VEIS LA CARA". Está en el
    vídeo pero puede que no se la vea. Se incluye porque la instrucción es "vídeos en los que
    aparece"; si el criterio fuera "que se la reconozca", quítese de la lista de abajo.

    CUÁNDO TIENE EFECTO
    -------------------
    NO lo tiene hasta que se publique la API. La versión que hay hoy en producción todavía no
    conoce FechaBaja y seguiría devolviendo estos vídeos en el listado. En cuanto se publique,
    desaparecen del listado, del buscador y de los correos post-compra, y su ficha responde 404.

    Marcarlos inactivos en la tienda NO vale: el sync los volvería a activar. Tiene que ser aquí.

    ES REVERSIBLE
    -------------
    Para devolverlos:  UPDATE Videos SET FechaBaja = NULL WHERE Id IN (...);
    No se borra nada. Esa es justo la diferencia con lo que hacíamos hasta esta semana.
*/

USE NV;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Noelia TABLE (Id int PRIMARY KEY);
INSERT INTO @Noelia (Id) VALUES
    (1314), (1393), (1432), (1475), (1489), (1516), (1538), (1718), (1719), (1787), (1798);

-- Comprobación previa: los 11 tienen que existir y estar vivos.
IF (SELECT COUNT(*) FROM @Noelia n JOIN Videos v ON v.Id = n.Id) <> 11
BEGIN
    RAISERROR('No están los 11 vídeos esperados. Se aborta.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM @Noelia n JOIN Videos v ON v.Id = n.Id WHERE v.FechaBaja IS NOT NULL)
BEGIN
    PRINT 'AVISO: alguno ya estaba de baja; no se le toca la fecha original.';
END

BEGIN TRANSACTION;

UPDATE v
SET v.FechaBaja = GETDATE()
FROM Videos v
JOIN @Noelia n ON n.Id = v.Id
WHERE v.FechaBaja IS NULL;

SELECT 'Vídeos retirados = ' + CAST(@@ROWCOUNT AS varchar);

IF (SELECT COUNT(*) FROM @Noelia n JOIN Videos v ON v.Id = n.Id WHERE v.FechaBaja IS NOT NULL) <> 11
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('No han quedado los 11 de baja: ROLLBACK.', 16, 1);
    RETURN;
END

COMMIT TRANSACTION;

-- Resultado
SELECT 'Vídeos vivos    = ' + CAST(COUNT(*) AS varchar) FROM Videos WHERE FechaBaja IS NULL;
SELECT 'Vídeos de baja  = ' + CAST(COUNT(*) AS varchar) FROM Videos WHERE FechaBaja IS NOT NULL;

SELECT 'Baja Id=' + CAST(v.Id AS varchar) + ' | ' + CONVERT(varchar(19), v.FechaBaja, 120)
     + ' | ' + LEFT(ISNULL(v.Titulo, ''), 50)
FROM Videos v JOIN @Noelia n ON n.Id = v.Id ORDER BY v.Id;
GO
