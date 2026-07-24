using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Perfiles
{
    // NestoAPI#258 — Fase 1: los perfiles de las agencias que HOY integramos, declarando su AgenciaId
    // y sus capacidades (las sub-interfaces IPerfilCon...). De momento son "cascarones": el registro
    // ya los descubre y deriva de ellos qué agencias tienen cada capacidad. En fases 2 y 3 las
    // capacidades ganarán métodos y se les moverá la lógica que hoy vive en FabricaAgenciasRemotas y
    // en EnviosAgenciasController. Al añadir una agencia bastará con crear su clase aquí.

    /// <summary>Innovatrans (DataTrans DTX): tramitación remota + seguimiento server-side.</summary>
    public class PerfilAgenciaInnovatrans : IPerfilConGestionRemota, IPerfilConSeguimiento
    {
        public int AgenciaId => Constantes.Agencias.AGENCIA_INNOVATRANS;
    }

    /// <summary>GLS/ASM: solo SEGUIMIENTO (no tramita server-side) y con defaults de envío propios.</summary>
    public class PerfilAgenciaGls : IPerfilConSeguimiento, IPerfilConDefaultsEnvio
    {
        public int AgenciaId => Constantes.Agencias.AGENCIA_GLS;
    }

    /// <summary>Canteras (Canarias, operativa manual): tiene reglas propias de compatibilidad con el destino.</summary>
    public class PerfilAgenciaCanteras : IPerfilConReglasDestino
    {
        public int AgenciaId => Constantes.Agencias.AGENCIA_CANTERAS;
    }
}
