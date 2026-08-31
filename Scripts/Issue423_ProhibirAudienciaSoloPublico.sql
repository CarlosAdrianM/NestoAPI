/*
    NestoAPI#423 (Slice 4) - prohibir AudienciaOferta = 3 ("solo publico").

    POR QUE
    -------
    AudienciaOferta solo existe en ProductoDTO: dice a quien se le PUBLICA el descuento en la
    tienda. GestorPrecios NO la mira — el motor de precios aplica el Descuento de la fila a todo
    el que pida, sea quien sea.

    Consecuencia: una fila marcada "solo publico" seria una mentira a medias. La tienda se lo
    ensenaria solo al publico final, pero cualquier profesional pidiendo por Nesto o por NestoApp
    se llevaria el mismo descuento igualmente, porque el motor no sabe distinguir. Y el sentido de
    #423 es justo el contrario: que la tienda anuncie lo que Nesto cobra.

    Los ambitos 1 (solo profesionales) y 2 (ambos) NO tienen ese problema: en los dos el
    profesional SI debe llevar el descuento, que es lo que el motor hace de todas formas.

    Decision de Carlos del 31/08/2026: prohibir el 3 POR AHORA, en vez de meter mano en el calculo
    de precios de todos los pedidos, que es el sitio mas delicado del sistema. Un descuento solo
    para el consumidor final es ademas raro en el negocio y no hace falta para el piloto de
    Ufaes/Pure. Si algun dia se necesita, el trabajo es que GestorPrecios respete la audiencia y
    entonces se retira esta restriccion.

    DONDE SE PROHIBE, Y POR QUE EN LA BD
    ------------------------------------
    Hoy no hay ninguna pantalla ni endpoint que escriba AudienciaOferta: esas filas se meten a
    mano por SQL. Asi que la BD es el UNICO sitio donde la prohibicion se puede hacer valer de
    verdad. Cuando exista la pantalla de campanas (Slice 5), validara ademas antes de guardar,
    para dar un mensaje decente en vez de un error de restriccion.

    El codigo de ProductoDTO.CalcularDescuentosPorAudiencia sigue sabiendo tratar el 3: queda como
    la definicion de la semantica por si se levanta la restriccion, y no estorba porque con este
    script no puede llegarle ninguna fila asi.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

-- =============================================================================
-- 1) COMPROBACION PREVIA: no puede haber ya filas con el 3
-- =============================================================================
PRINT '--- A) Reparto actual de AudienciaOferta (esperado: solo 0 y 1) ---';
SELECT AudienciaOferta,
       CASE AudienciaOferta WHEN 0 THEN 'no va a la web'
                            WHEN 1 THEN 'solo profesionales'
                            WHEN 2 THEN 'ambos'
                            WHEN 3 THEN 'solo publico (A PROHIBIR)'
                            ELSE 'VALOR DESCONOCIDO' END AS Significado,
       COUNT(*) AS Filas
FROM DescuentosProducto
GROUP BY AudienciaOferta
ORDER BY AudienciaOferta;

--  Si aparece alguna fila con 3 (o con un valor mayor), PARAR: hay que decidir que se hace con
--  ella antes de crear la restriccion, porque el ALTER fallaria. Lo razonable seria pasarla a 2
--  (que es lo que el motor cobra de verdad) despues de preguntar a quien la metio.

-- =============================================================================
-- 2) LA RESTRICCION
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DescuentosProducto_Audiencia')
BEGIN
    ALTER TABLE dbo.DescuentosProducto WITH CHECK
        ADD CONSTRAINT CK_DescuentosProducto_Audiencia
        CHECK (AudienciaOferta IN (0, 1, 2));
    PRINT 'Restriccion CK_DescuentosProducto_Audiencia creada.';
END
ELSE
    PRINT 'Restriccion CK_DescuentosProducto_Audiencia ya existia: no se toca.';
GO

-- =============================================================================
-- 3) COMPROBACION POSTERIOR
-- =============================================================================
PRINT '--- B) La restriccion existe, esta activa y es de fiar (is_disabled y is_not_trusted a 0) ---';
SELECT name, is_disabled, is_not_trusted, definition
FROM sys.check_constraints
WHERE name = 'CK_DescuentosProducto_Audiencia';

/*
    MARCHA ATRAS (el dia que GestorPrecios respete la audiencia y el 3 vuelva a tener sentido):

        ALTER TABLE dbo.DescuentosProducto DROP CONSTRAINT CK_DescuentosProducto_Audiencia;

    No hay nada mas que deshacer: el codigo que interpreta el 3 nunca se ha quitado.
*/
