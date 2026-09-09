# El descuento del Outlet pasa a venir de Nesto: hay que apagar las reglas de catálogo

09/09/2026 · Para el equipo del servidor de PrestaShop (producción). Continúa vuestro informe
"Reglas de precio del Outlet" del 09/09.

## Qué cambia

Hasta hoy el porcentaje del Outlet lo ponían las **36 reglas de catálogo** marca × categoría
Outlet que listasteis. A partir del corte que os avisaremos, ese porcentaje **lo manda Nesto**
por el canal que ya consumís desde la 1.8.0 del módulo: `DescuentoPorcentajeProfesional` y
`DescuentoPorcentajePublico` en el mensaje `Productos` (0-100, `null` = sin oferta, precios
plenos). Es exactamente el mismo mecanismo con el que se publicó la 41269 el 27/08.

Nesto va a republicar **todos los productos que están en las tres categorías de Outlet y cuya
marca tenía regla** (unos 400). Cada uno llegará con su porcentaje ya resuelto: el módulo
escribe el `specific_price` y no hay nada que interpretar por vuestra parte.

## Lo que os pedimos, en orden

### 0. Antes de tocar nada: foto para la marcha atrás

Exportad (`SELECT ... INTO OUTFILE` o dump) `ps_specific_price_rule`,
`ps_specific_price_rule_condition_group`, `ps_specific_price_rule_condition` y las filas de
`ps_specific_price` con `id_specific_price_rule <> 0`. Si hay que volver atrás, es reactivar
las reglas y regenerar.

### 1. Esperar a que llegue la republicación de Nesto

Os avisaremos cuando la cola de Nesto esté vacía. Comprobadlo por vuestro lado: en el log del
módulo deberían verse ~400 productos actualizados en una ventana de 15-30 minutos, todos con
`DescuentoPorcentajeProfesional` informado. Hasta que no estén **todos**, no apaguéis nada:
mientras convivan, el producto tiene dos fuentes de descuento y ganará la que PrestaShop
decida por prioridad.

Pregunta concreta que necesitamos respondida en este paso: **cuando para un mismo producto y
grupo existe un `specific_price` de regla (`id_specific_price_rule <> 0`) y el que escribe el
módulo, ¿cuál aplica PrestaShop?** Si gana la regla, la ventana de convivencia enseña el
precio viejo (no pasa nada); si gana el del módulo, el nuevo. En cualquier caso hay que
saberlo para interpretar lo que se ve en las fichas.

### 2. Desactivar (no borrar) las 36 reglas de Outlet

Poner `active = 0` (o fecha de fin ayer) en estas, que son las de vuestra tabla:

```
#239 agv · #249 Ainhoa · #177 #178 #179 #180 Anubis · #174 Ardell · #588 Belclinic
#590 BEOX · #240 Blanca de Barberá · #242 Cazcarra · #189 Cosméticos Foráneos
#182 CV Primary Essence · #589 Diapason · #592 Doman · #186 Du Cosmetics
#585 Eva Professional · #243 Faby · #236 Fama Fabre · #237 Giubra · #244 Greenik
#187 Irene Ríos · #591 Kach · #188 Lisap · #169 Masglo · #597 Maystar · #593 Mina
#586 ODA · #245 Paraíso · #251 Productos Genéricos · #587 Schwarzkopf · #247 Staleks
#594 Starpil · #246 Thuya · #595 Unión Láser · #596 Weelko
```

Desactivar una regla borra los `specific_price` que generó. **No toquéis los `specific_price`
que ha escrito el módulo** (los que no tienen `id_specific_price_rule`): son el descuento
nuevo.

Ojo con `#169 Masglo`: su condición también incluía *Esmaltes* y *Esmaltado permanente*, que
no son Outlet. Al apagarla, los esmaltes Masglo que no están en Outlet dejan de tener el 30 %.
Es lo esperado (Nesto solo manda el Outlet); si el negocio quiere mantener el 30 % en esmaltes
Masglo lo meteremos como campaña de familia en Nesto, no como regla aquí.

### 3. Las tres reglas "Outlet <marca>" que descontaban la marca entera

`#176 Outlet Moyra`, `#235 Outlet Jorge de la Garza` y `#248 Outlet Valmy` no filtran por
categoría. Nesto va a mandar el descuento **solo para los productos de esas marcas que están
en Outlet**. Desactivadlas también; el resto de la marca queda a precio pleno hasta que Carlos
decida si quiere una campaña de marca en Nesto. Si en ese punto veis que el negocio esperaba
otra cosa, decidlo antes de apagarlas.

`#584 Maystar Campaña Outlet Packs` (Pack Regalo) y `#538 IBD OUTLET` (Esmaltado permanente)
**no son Outlet**: dejadlas como están, no forman parte de este corte.

### 4. Las campañas viejas que seguían encendidas

Vuestro informe lo dice claro: sobre los productos de Outlet hay una pila de reglas de
*Black Friday 2025*, *Rebajas verano 2026*, *Rebajas* sin fecha de fin, más filas manuales de
`specific_price` con precio fijado para Profesionales (`#102`, `#312`, `#386` en Jorge de la
Garza; `#220`-`#222`, `#227`, `#395` en Maystar, y las que haya en las demás marcas).

Con Nesto como fuente, **cualquier regla o fila manual que siga viva es un precio que Nesto no
conoce y no cobra en el pedido**. Pedimos:

1. Inventario de todas las reglas activas que **no** sean de este corte, con nombre, %,
   condiciones y fecha de fin, y de las filas manuales de `specific_price` (sin regla y sin
   ser del módulo) sobre productos de Outlet.
2. Desactivar las que sean de campañas pasadas (BF 2025, rebajas de verano, "Rebajas" sin
   fin). Las que no sepáis clasificar, en la lista para que Carlos decida.

### 5. Verificación

Tras apagar las reglas, limpiad caché y comprobad una ficha por marca **como anónimo y como
un usuario del grupo Profesionales**. Lo que debe verse (tachado + porcentaje, precio pleno
como base):

| Marca | Anónimo / Predeterminado | Profesionales |
|---|---|---|
| agv, Kach, Lisap | 35 % | 35 % |
| Schwarzkopf | 50 % | 50 % |
| Jorge de la Garza (solo Outlet) | 40 % | 40 % |
| Ardell, Cosméticos Foráneos, Doman, Masglo | 30 % | 30 % |
| Anubis Cosmetics | 25 % | 30,89 % |
| Blanca de Barberá, CV, Faby, Fama Fabre, Giubra, Irene Ríos, Moyra, Productos Genéricos (Outlet Peluquería), Thuya, Valmy (solo Outlet) | 25 % | 25 % |
| Ainhoa, Belclinic, BEOX, Cazcarra, Diapason, Du Cosmetics, Greenik, Mina | 20 % | 20 % |
| Eva Professional, Maystar | 15 % | 15 % |
| ODA, Paraíso Cosmetics | 10 % | 10 % |
| Staleks, Starpil, Unión Láser, Weelko | **sin descuento** | 15 % |

Los cinco casos que en vuestro informe salían "no coincide" o "no aplica" sin motivo
(Belclinic, Maystar, Productos Genéricos, Doman, Paraíso) tienen que quedar en el valor de la
tabla: es el efecto de haber quitado la pila de reglas viejas.

Devolvednos la tabla rellena con lo que se ve (referencia comprobada, % anónimo, %
profesional) y el inventario del punto 4.

### 6. Una comprobación aparte sobre categorías

Al republicar ~400 productos por el bus, confirmad que **ninguno pierde su categoría de
Outlet** en la tienda. El mensaje lleva `CategoriasSecundarias` (#414); si vuestra versión del
módulo aún no las consume (ps#12), el update no debe tocar las asociaciones de categoría. Si
detectáis que alguna ficha sale del Outlet tras la republicación, parad y avisad: es un
problema del módulo, no del descuento.

## Qué NO cambia

- Precios plenos (`PrecioProfesional`, `PrecioPublicoFinal`) siguen igual que desde el
  cutover del 26/08.
- `nv_group.reduction` del grupo Profesionales: no lo tocamos en este corte.
- Las categorías de Outlet en sí (Estética, Peluquería, Maquillaje y Uñas) siguen existiendo
  y siguen viniendo de Nesto.
