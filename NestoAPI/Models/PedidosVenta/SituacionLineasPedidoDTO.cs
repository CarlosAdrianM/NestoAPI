namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// Nesto#340 (slice A3): las dos preguntas que el módulo de Agencias le hacía a las LÍNEAS de
    /// un pedido abriendo su propio DbContext. Son las únicas dos, y ninguna necesita las líneas:
    /// solo un sí o un no.
    ///
    /// Van juntas en un DTO —aunque cada llamante use una sola— porque son la misma pregunta
    /// ("cómo está este pedido") hecha desde dos sitios del mismo flujo de tramitación, y así se
    /// resuelven en una llamada si algún día coinciden. Añadir campos aquí es barato; abrir un
    /// endpoint por booleano, no.
    ///
    /// Que el cálculo viva en el servidor tiene un premio que no es solo quitar EF: la comparación
    /// vuelve a hacerla SQL Server, que ignora el relleno de los char. La versión que se sustituye
    /// también lo hacía (era LINQ to Entities), pero si se hubiera resuelto trayendo las líneas y
    /// filtrando en memoria habríamos caído otra vez en el fallo silencioso de Nesto#254.
    /// </summary>
    public class SituacionLineasPedidoDTO
    {
        /// <summary>
        /// Alguna línea viva (estado entre PENDIENTE y EN_CURSO) tiene picking asignado.
        ///
        /// Agencias lo usa en negativo: si NO hay ninguna, pregunta al usuario si de verdad quiere
        /// insertar el envío. O sea que un false de más molesta, y un false de menos deja pasar en
        /// silencio un pedido que nadie ha preparado.
        /// </summary>
        public bool TieneAlgunaLineaConPicking { get; set; }

        /// <summary>
        /// TODAS las líneas del pedido son de un canal externo (Amazon, tienda online, Perfume's
        /// Club, Miravia). Sin filtro de estado: se miran todas, como hacía el original.
        ///
        /// ⚠️ Un pedido SIN NINGUNA LÍNEA cuenta como "todo online" (es lo que devuelve un All
        /// sobre una lista vacía, y es el comportamiento que se está sustituyendo). Se conserva a
        /// propósito y hay un test que lo fija: el efecto es no mandar el correo de aviso de
        /// entrega, que en un pedido vacío es lo que se quiere.
        /// </summary>
        public bool EsTodoOnline { get; set; }
    }
}
