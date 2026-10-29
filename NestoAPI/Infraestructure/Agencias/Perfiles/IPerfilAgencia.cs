using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Perfiles
{
    /// <summary>
    /// NestoAPI#258: perfil de UNA agencia de transporte. Reúne en una sola clase todo lo específico
    /// de esa agencia (tramitación remota, seguimiento, reglas de destino, defaults de envío...),
    /// declarado mediante las sub-interfaces de capacidad que implemente. Objetivo: "integrar una
    /// agencia = escribir solo su clase de perfil". <see cref="RegistroAgencias"/> las descubre por
    /// reflexión y el resto del código (fábrica remota, comparador, controller de envíos) las
    /// consulta por <see cref="AgenciaId"/> o por capacidad, en lugar de con switch/if.
    ///
    /// Contrato: cada perfil DEBE tener constructor sin parámetros (se instancia por reflexión) y
    /// haber UNA sola clase por <see cref="AgenciaId"/> (el registro aborta si encuentra dos).
    /// </summary>
    public interface IPerfilAgencia
    {
        /// <summary>Número de la agencia en AgenciasTransporte (ver <see cref="Constantes.Agencias"/>).</summary>
        int AgenciaId { get; }
    }

    /// <summary>
    /// Capacidad: la agencia se TRAMITA server-side (insertar envío + obtener etiqueta). El perfil
    /// compone la estrategia completa (cliente + operaciones + decorador de reintentos); recibe la BD
    /// porque algunas agencias leen su configuración de AgenciasTransporte (p.ej. Identificador).
    /// </summary>
    public interface IPerfilConGestionRemota : IPerfilAgencia
    {
        IAgenciaRemota CrearGestionRemota(NVEntities db);
    }

    /// <summary>
    /// NestoAPI#552 (fase 3): capacidad de ANULAR en el servidor un envío ya registrado, para una agencia que NO tiene
    /// gestión remota completa (hoy GLS: su tramitación sigue en Nesto o, con su interruptor, a medias en la API). Las de
    /// gestión remota completa no la necesitan: su <see cref="IAgenciaRemota"/> ya anula. Devuelve null si la agencia no
    /// debe anular por la API ahora mismo (interruptor apagado): el llamante sigue como si no tuviera la capacidad.
    /// </summary>
    public interface IPerfilConAnulacionRemota : IPerfilAgencia
    {
        IAnulacionAgenciaRemota CrearAnulacion(NVEntities db);
    }

    /// <summary>
    /// Capacidad: la agencia expone SEGUIMIENTO (consultar el estado de un envío por su albarán). La
    /// cumplen tanto las de tramitación (Innovatrans, cuya estrategia de tramitar ya sigue) como las
    /// que solo siguen (GLS).
    /// </summary>
    public interface IPerfilConSeguimiento : IPerfilAgencia
    {
        ISeguimientoAgenciaRemota CrearSeguimiento(NVEntities db);
    }

    /// <summary>
    /// Capacidad: la agencia tiene REGLAS propias de compatibilidad con el destino (Canteras solo
    /// Canarias y sin reembolso, CEX no entrega en Canarias...). Estas reglas aplican a CUALQUIER
    /// etiqueta que se cree, esté la agencia en cuarentena o no (por eso el controller las consulta
    /// en el registro SIN puerta).
    /// </summary>
    public interface IPerfilConReglasDestino : IPerfilAgencia
    {
        /// <summary>Mensaje de error si la combinación agencia+destino no es válida, o null si lo es.</summary>
        string ValidarDestino(string codPostal, CabPedidoVta pedido, bool cobrarReembolso);
    }

    /// <summary>
    /// Capacidad: la agencia tiene valores por DEFECTO de envío propios (servicio, horario, país)
    /// según el código postal. Como las reglas de destino, aplican con o sin cuarentena.
    /// </summary>
    public interface IPerfilConDefaultsEnvio : IPerfilAgencia
    {
        (short Servicio, short Horario, int Pais) DefaultsEnvio(string codPostal);
    }

    /// <summary>
    /// NestoAPI#739: la agencia no sirve pedidos de canales externos (Amazon, tienda online): no tiene transportista en
    /// Prestashop ni código en Amazon (su enlace de seguimiento es nuestra propia página, NestoAPI#744). Hoy, el reparto
    /// propio (rutas AT y 16). La guardia de cobertura de seguimiento (CoberturaSeguimientoAgenciasTests) no le exige datos
    /// de canal.
    /// </summary>
    public interface IPerfilSinCanalesExternos : IPerfilAgencia
    {
    }
}
