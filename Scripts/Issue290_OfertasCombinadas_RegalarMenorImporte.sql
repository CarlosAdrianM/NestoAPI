-- Issue #290: ofertas combinadas con la regla "la unidad regalada debe ser la de menor importe".
--
-- Para ofertas 2+1 combinables entre referencias de PRECIOS DISTINTOS (grupos de alternativas
-- o filtros): el usuario combina como quiera (3 iguales, 3 distintas, 2+1...), pero la unidad
-- que va a base 0 debe ser la de menor tarifa (PVP) del conjunto, y las pagadas deben cubrir
-- su tarifa (suelo de importe DINÁMICO por combinación, en vez del ImporteMinimo fijo).
--
-- ⚠️ EJECUTAR EN PRODUCCIÓN ANTES DE DESPLEGAR LA API (el EDMX ya mapea la columna).

ALTER TABLE OfertasCombinadas ADD RegalarMenorImporte bit NOT NULL
    CONSTRAINT DF_OfertasCombinadas_RegalarMenorImporte DEFAULT 0;
GO
