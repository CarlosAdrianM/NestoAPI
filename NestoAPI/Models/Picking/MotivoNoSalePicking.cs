namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// NestoAPI#608: por qué un pedido se ha quedado fuera del picking en esta pasada. Solo EXPLICA la decisión
    /// (la toman <see cref="PedidoPicking.saleEnPicking"/> y el bucle de GestorPicking); en el picking de UN pedido
    /// se convierte en un mensaje concreto en vez del genérico «No hay stock suficiente…».
    /// </summary>
    public enum MotivoNoSalePicking
    {
        /// <summary>Sale (o aún no se ha evaluado).</summary>
        Ninguno = 0,
        /// <summary>No le queda ninguna línea en esta pasada (p. ej. todas con fecha de entrega posterior).</summary>
        SinLineas,
        /// <summary>Modo 3: le tiene que llegar stock suyo de las tiendas (GestorReposicionTiendas).</summary>
        EsperaReposicionDeTiendas,
        /// <summary>Modo 1 («Todo junto») y falta algo.</summary>
        TodoJuntoSinStock,
        /// <summary>Modo 4 con la primera entrega ya hecha: el resto sale de una vez y falta algo.</summary>
        RestoDeUnaVezSinStock,
        /// <summary>NestoAPI#529: lo de pago tiene stock, lo que falta son solo regalos (base 0) y no se sirve a medias.</summary>
        EsperaSoloRegalos,
        /// <summary>Prepago sin cubrir.</summary>
        RetenidoPorPrepago,
        /// <summary>No hay stock de nada que se cobre (HayStockDeAlgo falso).</summary>
        SinStockDePago
    }
}
