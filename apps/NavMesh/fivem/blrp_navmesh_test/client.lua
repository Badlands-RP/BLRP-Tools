-- Developer test resource. One local NPC; results go to the invoking client's F8 console.
local startPoint, goalPoint, ped, running, busy
local function coords(v) return { x = v.x, y = v.y, z = v.z } end
local function log(value) print('[navmesh-test] ' .. json.encode(value)) end
local function cleanup()
    if ped and DoesEntityExist(ped) then DeleteEntity(ped) end
    ped = nil
end

RegisterCommand('navtest_start', function()
    startPoint = GetEntityCoords(PlayerPedId())
    log({ start = coords(startPoint) })
end, false)

RegisterCommand('navtest_goal', function()
    goalPoint = GetEntityCoords(PlayerPedId())
    log({ goal = coords(goalPoint) })
end, false)

RegisterCommand('navtest_cancel', function() running = false end, false)

RegisterCommand('navtest_run', function(_, args)
    if busy then return log({ error = 'A route test is already running or cleaning up.' }) end
    if not startPoint or not goalPoint or #(goalPoint - startPoint) < 2 or #(goalPoint - startPoint) > 200 then
        return log({ error = 'Set start and goal positions 2–200 metres apart with navtest_start and navtest_goal.' })
    end
    local origin, target = startPoint, goalPoint
    local candidate = args[2] or 'blrp_navmesh_candidate'
    local result = {
        label = args[1] or 'unlabelled', gameBuild = GetGameBuildNumber(),
        candidate = candidate, candidateState = GetResourceState(candidate),
        bakeId = GetResourceMetadata(candidate, 'navmesh_bake', 0),
        targetBuild = GetResourceMetadata(candidate, 'navmesh_target_build', 0),
        start = coords(origin), goal = coords(target), trace = {}
    }
    running = true
    busy = true
    CreateThread(function()
        local ok, err = pcall(function()
            local model = joaat('a_m_m_business_01')
            RequestModel(model)
            local deadline = GetGameTimer() + 10000
            while running and not HasModelLoaded(model) and GetGameTimer() < deadline do Wait(50) end
            if not HasModelLoaded(model) then error('NPC model did not load.') end
            if not running then SetModelAsNoLongerNeeded(model) return end
            cleanup()
            ped = CreatePed(4, model, origin.x, origin.y, origin.z, 0.0, false, false)
            SetModelAsNoLongerNeeded(model)
            if not DoesEntityExist(ped) then error('NPC could not be created.') end
            SetEntityAsMissionEntity(ped, true, true)
            SetEntityInvincible(ped, true)
            SetBlockingOfNonTemporaryEvents(ped, true)
            RequestCollisionAtCoord(origin.x, origin.y, origin.z)
            deadline = GetGameTimer() + 5000
            while running and not HasCollisionLoadedAroundEntity(ped) and GetGameTimer() < deadline do Wait(50) end
            if not HasCollisionLoadedAroundEntity(ped) then error('Collision did not load at the start.') end
            if not running then return end
            -- Default navmesh flags, with no direct-movement fallback.
            TaskFollowNavMeshToCoord(ped, target.x, target.y, target.z, 1.0, 60000, 0.4, 0, 0.0)
            local began, previous, travelled = GetGameTimer(), GetEntityCoords(ped), 0.0
            while running and DoesEntityExist(ped) and GetGameTimer() - began < 60000 do
                Wait(500)
                if not DoesEntityExist(ped) then break end
                local position = GetEntityCoords(ped)
                travelled = travelled + #(position - previous)
                previous = position
                local distance = #(target - position)
                local sample = { ms = GetGameTimer() - began, position = coords(position),
                    routeResult = GetNavmeshRouteResult(ped), distance = distance }
                result.trace[#result.trace + 1] = sample
                if distance < 0.75 and travelled > 1.0 then result.arrived = true break end
            end
            result.travelled = travelled
            result.elapsedMs = GetGameTimer() - began
            result.cancelled = not running
            result.arrived = result.arrived or false
        end)
        if not ok then result.error = tostring(err) end
        cleanup()
        running = false
        busy = false
        log(result)
    end)
end, false)

AddEventHandler('onResourceStop', function(resource)
    if resource == GetCurrentResourceName() then running = false cleanup() end
end)
