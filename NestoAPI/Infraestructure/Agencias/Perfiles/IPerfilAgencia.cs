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
    /// Capacidad: la agencia se TRAMITA server-side (insertar envío + obtener etiqueta). Fase 2 de
    /// #258 le añadirá el método de composición; por ahora es un marcador con el que el registro
    /// deriva qué agencias tienen gestión remota (hoy hardcodeado en FabricaAgenciasRemotas).
    /// </summary>
    public interface IPerfilConGestionRemota : IPerfilAgencia { }

    /// <summary>
    /// Capacidad: la agencia expone SEGUIMIENTO (consultar el estado de un envío por su albarán). La
    /// cumplen tanto las de tramitación (Innovatrans) como las que solo siguen (GLS).
    /// </summary>
    public interface IPerfilConSeguimiento : IPerfilAgencia { }

    /// <summary>
    /// Capacidad: la agencia tiene REGLAS propias de compatibilidad con el destino (Canteras solo
    /// Canarias y sin reembolso, CEX no entrega en Canarias...). Fase 3.
    /// </summary>
    public interface IPerfilConReglasDestino : IPerfilAgencia { }

    /// <summary>
    /// Capacidad: la agencia tiene valores por DEFECTO de envío propios (servicio, horario, país)
    /// según el código postal. Fase 3.
    /// </summary>
    public interface IPerfilConDefaultsEnvio : IPerfilAgencia { }
}
