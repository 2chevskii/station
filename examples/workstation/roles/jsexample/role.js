import {copy, template} from 'station:modules/builtin'
import {filedest} from 'station:vars'

task('Example JS task', copy, {
    source: 'myfile.txt',
    destination: filedest
});

// task without explicit name
task(template, {source: 'mytemplate.txt', destination: '/my/template.txt'});