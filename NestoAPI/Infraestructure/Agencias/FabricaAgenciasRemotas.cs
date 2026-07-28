using System;
using System.Collections.Generic;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    public interface IFabricaAgenciasRemotas
    {
        /// <summary>
        /// Devuelve la estrategia de gestión remota de la agencia, o <c>null</c> si esa agencia no
        /// tiene integración (Canteras, GLS de momento, etc.): el llamante hace solo el flujo de BD.
        /// </summary>
        IAgenciaRemota Crear(int agenciaId);

        /// <summary>
        /// Ids de las agencias con TRAMITACIÓN remota server-side (las que <see cref="Crear"/> construye).
        /// </summary>
        IReadOnlyCollection<int> AgenciasConGestionRemota { get; }

        /// <summary>
        /// Estrategia de SEGUIMIENTO (solo lectura) de la agencia, o null si no tiene. La cumplen tanto
        /// las de tramitación (Innovatrans) como las que solo siguen (GLS). La usa el poll de estados.
        /// </summary>
        ISeguimientoAgenciaRemota CrearSeguimiento(int agenciaId);

        /// <summary>
        /// Ids de las agencias con SEGUIMIENTO (las que <see cref="CrearSeguimiento"/> construye). El poll
        /// filtra por aquí para no recorrer agencias sin integración.
        /// </summary>
        IReadOnlyCollection<int> AgenciasConSeguimiento { get; }
    }

    /// <summary>
    /// Factory de agencias con gestión remota. NestoAPI#258 (Fase 2): ya no hay if por agencia ni
    /// arrays hardcodeados; delega en <see cref="RegistroAgencias"/> (perfiles descubiertos por
    /// reflexión y filtrados por la puerta de activas). La composición de cada agencia vive en su
    /// clase de perfil (PerfilesAgencias.cs): añadir una agencia = escribir su perfil, sin tocar aquí.
    /// El registro se construye perezosamente porque la puerta lee la BBDD (AgenciasTransporte +
    /// parámetro AgenciasEnCuarentena) y no todos los usos del controller lo necesitan.
    /// </summary>
    public class FabricaAgenciasRemotas : IFabricaAgenciasRemotas
    {
        private readonly NVEntities _db;
        private readonly Lazy<RegistroAgencias> _registro;

        public FabricaAgenciasRemotas(NVEntities db) : this(db, null)
        {
        }

        // Para tests: permite inyectar un registro ya construido (perfiles y puerta controlados).
        internal FabricaAgenciasRemotas(NVEntities db, RegistroAgencias registro)
        {
            _db = db;
            _registro = new Lazy<RegistroAgencias>(() =>
                registro ?? RegistroAgencias.PorReflexion(new GateAgenciasActivasPorCuarentena(_db)));
        }

        public IReadOnlyCollection<int> AgenciasConGestionRemota => _registro.Value.ConCapacidad<IPerfilConGestionRemota>();

        public IReadOnlyCollection<int> AgenciasConSeguimiento => _registro.Value.ConCapacidad<IPerfilConSeguimiento>();

        public IAgenciaRemota Crear(int agenciaId)
            => (_registro.Value.Perfil(agenciaId) as IPerfilConGestionRemota)?.CrearGestionRemota(_db);

        public ISeguimientoAgenciaRemota CrearSeguimiento(int agenciaId)
            => (_registro.Value.Perfil(agenciaId) as IPerfilConSeguimiento)?.CrearSeguimiento(_db);
    }
}
