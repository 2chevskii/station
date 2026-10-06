task('Copy the Lua example', modules.copy, {
    file = 'example.txt',
    destination = vars.lua_example_destination
})
